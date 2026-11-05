using System.Globalization;
using System.Text;

namespace GPDuel;

// One spawn pair of an arena: where the two players stand
public sealed class SpawnPair
{
    public float[] CTPos = new float[3];
    public float[] CTAng = new float[3];
    public float[] TPos = new float[3];
    public float[] TAng = new float[3];
}

public sealed class Arena
{
    public int Id;
    public List<SpawnPair> Pairs = [];
    // Duel or waiting (solo) player that uses the arena; null = free
    public object? Owner;
}

// Spawn files (spawns/<map>.txt) - the same "Positions" format as ZaadrotDuel / Multi-1v1:
// "Positions" { "BlockDefaultSpawn" "0"  "<arena>" { "<pair>" { "CTSpawnPos" "x y z" "CTSpawnAngle" "p y r" "TSpawnPos" ... } } }
public static class ArenaFile
{
    private sealed class KvNode
    {
        public string? Value;
        public List<(string Key, KvNode Node)> Children = [];
    }

    public static List<Arena> Load(string path)
    {
        var arenas = new List<Arena>();
        if (!File.Exists(path)) return arenas;

        var tokens = Tokenize(File.ReadAllText(path));
        int i = 0;
        var root = ParseBlock(tokens, ref i, topLevel: true);

        // the file is "Positions" { ... }
        var positions = root.Children.FirstOrDefault(c => c.Node.Value == null).Node;
        if (positions == null) return arenas;

        foreach (var (arenaKey, arenaNode) in positions.Children)
        {
            if (arenaNode.Value != null || !int.TryParse(arenaKey, out int arenaId)) continue;

            var arena = new Arena { Id = arenaId };
            foreach (var (_, pairNode) in arenaNode.Children)
            {
                if (pairNode.Value != null) continue;
                var pair = new SpawnPair();
                bool ok = true;
                ok &= ReadVec(pairNode, "CTSpawnPos", pair.CTPos);
                ok &= ReadVec(pairNode, "CTSpawnAngle", pair.CTAng);
                ok &= ReadVec(pairNode, "TSpawnPos", pair.TPos);
                ok &= ReadVec(pairNode, "TSpawnAngle", pair.TAng);
                if (ok) arena.Pairs.Add(pair);
            }
            if (arena.Pairs.Count > 0) arenas.Add(arena);
        }
        return arenas;
    }

    public static void Save(string path, List<Arena> arenas)
    {
        var sb = new StringBuilder();
        sb.Append("\"Positions\"\n{\n\t\"BlockDefaultSpawn\"\t\t\"0\"\n");
        for (int a = 0; a < arenas.Count; a++)
        {
            sb.Append($"\t\"{a}\"\n\t{{\n");
            for (int p = 0; p < arenas[a].Pairs.Count; p++)
            {
                var pair = arenas[a].Pairs[p];
                sb.Append($"\t\t\"{p}\"\n\t\t{{\n");
                sb.Append($"\t\t\t\"CTSpawnPos\"\t\t\"{Vec(pair.CTPos)}\"\n");
                sb.Append($"\t\t\t\"CTSpawnAngle\"\t\t\"{Vec(pair.CTAng)}\"\n");
                sb.Append($"\t\t\t\"TSpawnPos\"\t\t\"{Vec(pair.TPos)}\"\n");
                sb.Append($"\t\t\t\"TSpawnAngle\"\t\t\"{Vec(pair.TAng)}\"\n");
                sb.Append("\t\t}\n");
            }
            sb.Append("\t}\n");
        }
        sb.Append("}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }

    private static string Vec(float[] v) =>
        string.Join(" ", v.Select(f => f.ToString("F6", CultureInfo.InvariantCulture)));

    private static bool ReadVec(KvNode node, string key, float[] into)
    {
        var value = node.Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)).Node?.Value;
        if (value == null) return false;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        for (int i = 0; i < 3; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out into[i])) return false;
        return true;
    }

    private static KvNode ParseBlock(List<string> t, ref int i, bool topLevel = false)
    {
        var node = new KvNode();
        while (i < t.Count)
        {
            var tok = t[i++];
            if (tok == "}") { if (!topLevel) break; continue; }
            if (tok == "{") continue; // stray brace

            string key = tok;
            if (i >= t.Count) break;
            if (t[i] == "{")
            {
                i++;
                node.Children.Add((key, ParseBlock(t, ref i)));
            }
            else
            {
                node.Children.Add((key, new KvNode { Value = t[i++] }));
            }
        }
        return node;
    }

    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '{' || c == '}') { tokens.Add(c.ToString()); i++; continue; }
            if (c == '"')
            {
                int end = s.IndexOf('"', i + 1);
                if (end < 0) end = s.Length;
                tokens.Add(s.Substring(i + 1, end - i - 1));
                i = end + 1;
                continue;
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
            tokens.Add(s.Substring(start, i - start));
        }
        return tokens;
    }
}
