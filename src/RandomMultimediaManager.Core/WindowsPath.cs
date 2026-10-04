namespace RandomMultimediaManager.Core;

// Pure Windows syntax: never consult the current directory, filesystem, or network.
public sealed record WindowsPath(string Path, string PathKey, string Root, string RootKey)
{
    public static WindowsPath Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input != input.Trim())
            throw new ArgumentException("절대 경로를 입력하세요.");
        string path = input.Replace('/', '\\');
        string root;
        string remainder;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('\\');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
                throw new ArgumentException("UNC 서버와 공유 이름이 필요합니다.");
            ValidateComponent(parts[0]); ValidateComponent(parts[1]);
            root = @"\\" + parts[0] + "\\" + parts[1] + "\\";
            remainder = string.Join("\\", parts.Skip(2));
        }
        else
        {
            if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
                throw new ArgumentException("드라이브 또는 UNC 절대 경로가 필요합니다.");
            root = path[..3]; remainder = path[3..];
        }
        var components = new List<string>();
        foreach (string part in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (components.Count == 0) throw new ArgumentException("루트 밖으로 나갈 수 없습니다.");
                components.RemoveAt(components.Count - 1); continue;
            }
            ValidateComponent(part); components.Add(part);
        }
        string normalized = root + string.Join("\\", components);
        return new(normalized, normalized.ToUpperInvariant(), root, root.ToUpperInvariant());
    }

    private static void ValidateComponent(string part)
    {
        if (part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')
            || part.Any(c => c < 32 || "<>:\"|?*".Contains(c)))
            throw new ArgumentException("지원하지 않는 경로 구성요소입니다.");
    }

    public static bool IsSameOrDescendant(string path, string parent)
    {
        var child = Normalize(path); var ancestor = Normalize(parent);
        return child.RootKey == ancestor.RootKey && (child.PathKey == ancestor.PathKey
            || child.PathKey.StartsWith(ancestor.PathKey.TrimEnd('\\') + "\\", StringComparison.Ordinal));
    }
}
