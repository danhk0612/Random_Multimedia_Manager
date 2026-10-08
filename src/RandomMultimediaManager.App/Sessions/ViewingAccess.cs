using System.IO;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Scanning;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Sessions;

// Owned by one viewing window under its whole-lifetime scan exclusive lease. Metadata only;
// no directory enumeration, media content, durable availability, or automatic reconnection.
public sealed class ViewingAccess
{
    private readonly LibraryDatabase database;
    private readonly Func<string, CancellationToken, Task<StorageObservation>> observe;
    private readonly Func<string, CancellationToken, Task> accessible;
    private readonly Func<StorageBinding, long?> confirmed;
    private readonly Func<bool> closing;
    private readonly Dictionary<string, BindingVerification> roots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Revision, long? Generation)> disconnected = new(StringComparer.Ordinal);

    public sealed record Ticket(StorageBinding Binding, long Generation);
    public ViewingAccess(LibraryDatabase database, Func<StorageBinding, long?>? confirmed = null,
        Func<bool>? closing = null,
        Func<string, CancellationToken, Task<StorageObservation>>? observe = null,
        Func<string, CancellationToken, Task>? accessible = null)
    {
        this.database = database;
        this.confirmed = confirmed ?? (_ => null);
        this.closing = closing ?? (() => false);
        this.observe = observe ?? WindowsStorageProbe.ObserveAsync;
        this.accessible = accessible ?? ((path, token) => Task.Run(() =>
        { token.ThrowIfCancellationRequested(); _ = File.GetAttributes(path); token.ThrowIfCancellationRequested(); }, token));
    }

    public async Task<IReadOnlySet<Guid>> AvailableSourcesAsync(IEnumerable<CategorySource> sources,
        CancellationToken token)
    {
        var available = new HashSet<Guid>();
        // One set read per candidate decision, never a binding query per item/source.
        var bindings = await Task.Run(database.GetStorageBindings);
        foreach (var group in sources.Where(s => s.IsEnabled).GroupBy(s => WindowsPath.Normalize(s.RootPath).RootKey))
        {
            token.ThrowIfCancellationRequested();
            if (closing()) throw new OperationCanceledException();
            var binding = bindings.SingleOrDefault(b => b.RootKey == group.Key);
            if (binding is null) continue;
            Ticket? ticket;
            try { ticket = await VerifyAsync(group.First().RootPath, binding, token); }
            catch (OperationCanceledException) { throw; }
            catch { continue; }
            if (ticket is null) continue;
            var observed = new List<Guid>();
            foreach (var source in group)
            {
                token.ThrowIfCancellationRequested();
                if (closing()) throw new OperationCanceledException();
                try
                {
                    await accessible(source.RootPath, token);
                    observed.Add(source.Id);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* A failed nested source does not invalidate an accessible sibling. */ }
            }
            if (observed.Count == 0) Invalidate(ticket.Binding);
            else if (IsCurrent(ticket)) available.UnionWith(observed);
        }
        return available;
    }

    public async Task<Ticket> CheckAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closing()) throw new OperationCanceledException();
        var binding = await Task.Run(() => database.GetStorageBinding(path))
            ?? throw new IOException("대상 바인딩이 없습니다.");
        try
        {
            var ticket = await VerifyAsync(path, binding, token)
                ?? throw new IOException("연결 대상이 변경됐거나 동일 대상 확인이 필요합니다.");
            await accessible(path, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(ticket)) throw new IOException("오래된 연결 결과입니다.");
            return ticket;
        }
        catch (OperationCanceledException) { throw; }
        catch { Invalidate(binding); throw; }
    }

    private async Task<Ticket?> VerifyAsync(string path, StorageBinding binding, CancellationToken token)
    {
        if (closing()) throw new OperationCanceledException();
        var evidence = await observe(path, token);
        token.ThrowIfCancellationRequested();
        if (closing()) throw new OperationCanceledException();
        if (database.GetStorageBinding(path) != binding) return null;
        if (binding.RequiresConfirmation && evidence.Kind == StorageKind.Local)
            binding = database.ConfirmStorageBinding(path, binding.Revision, evidence, false);
        if (!roots.TryGetValue(binding.RootKey, out var verification) || verification.Binding != binding)
            roots[binding.RootKey] = verification = new(binding);
        long generation = verification.Generation;
        long? confirmation = confirmed(binding);
        bool userConfirmed = confirmation is not null && (!disconnected.TryGetValue(binding.RootKey, out var old)
            || old != (binding.Revision, confirmation));
        if (!verification.Observe(generation, evidence, userConfirmed)) return null;
        return new(binding, generation);
    }

    public bool IsCurrent(Ticket ticket) => !closing()
        && roots.TryGetValue(ticket.Binding.RootKey, out var verification)
        && verification.Binding == ticket.Binding
        && verification.CanAccess(ticket.Binding.RootKey, ticket.Generation)
        && database.GetStorageBinding(ticket.Binding.RootKey) == ticket.Binding;

    public async Task<bool> RecheckAsync(string path, Ticket ticket, CancellationToken token)
    {
        if (!IsCurrent(ticket)) return false;
        var after = await CheckAsync(path, token);
        return after == ticket && IsCurrent(ticket);
    }
    private void Invalidate(StorageBinding binding)
    {
        if (roots.TryGetValue(binding.RootKey, out var verification)) verification.Invalidate();
        if (binding.RequiresConfirmation) disconnected[binding.RootKey] = (binding.Revision, confirmed(binding));
    }
}
