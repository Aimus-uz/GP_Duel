using MySqlConnector;

namespace GPDuel;

public sealed class PlayerStats
{
    public string Name = "";
    public int Wins, Losses, Kills, Deaths, Duels;
}

// Same tables as ZaadrotDuel (duel_stats, zw_duel_prefs) - old statistics and weapon choices are kept
public sealed class Database
{
    private readonly string _conn;
    public bool Enabled { get; }

    public Database(DatabaseConfig cfg)
    {
        Enabled = !string.IsNullOrWhiteSpace(cfg.Host) && !string.IsNullOrWhiteSpace(cfg.Database);
        _conn = new MySqlConnectionStringBuilder
        {
            Server = cfg.Host,
            Port = cfg.Port,
            UserID = cfg.User,
            Password = cfg.Password,
            Database = cfg.Database,
            Pooling = true,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10,
        }.ConnectionString;
    }

    private async Task<MySqlConnection> Open()
    {
        var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        return c;
    }

    public async Task Init()
    {
        if (!Enabled) return;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS duel_stats (
    steamid BIGINT UNSIGNED PRIMARY KEY,
    name VARCHAR(64) NOT NULL DEFAULT '',
    wins INT NOT NULL DEFAULT 0,
    losses INT NOT NULL DEFAULT 0,
    kills INT NOT NULL DEFAULT 0,
    deaths INT NOT NULL DEFAULT 0,
    duels INT NOT NULL DEFAULT 0,
    updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS zw_duel_prefs (
    steamid BIGINT UNSIGNED PRIMARY KEY,
    prim VARCHAR(40) NOT NULL DEFAULT 'weapon_ak47',
    sec  VARCHAR(40) NOT NULL DEFAULT 'weapon_hkp2000',
    updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Add(ulong steamId, string name, int wins, int losses, int kills, int deaths, int duels)
    {
        if (!Enabled) return;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO duel_stats (steamid, name, wins, losses, kills, deaths, duels)
VALUES (@s, @n, @w, @l, @k, @d, @u)
ON DUPLICATE KEY UPDATE name=@n, wins=wins+@w, losses=losses+@l, kills=kills+@k, deaths=deaths+@d, duels=duels+@u;";
        cmd.Parameters.AddWithValue("@s", steamId);
        cmd.Parameters.AddWithValue("@n", Trim(name));
        cmd.Parameters.AddWithValue("@w", wins);
        cmd.Parameters.AddWithValue("@l", losses);
        cmd.Parameters.AddWithValue("@k", kills);
        cmd.Parameters.AddWithValue("@d", deaths);
        cmd.Parameters.AddWithValue("@u", duels);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<PlayerStats?> GetStats(ulong steamId)
    {
        if (!Enabled) return null;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name, wins, losses, kills, deaths, duels FROM duel_stats WHERE steamid=@s;";
        cmd.Parameters.AddWithValue("@s", steamId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return Read(r);
    }

    public async Task<List<PlayerStats>> GetTop(int count)
    {
        var list = new List<PlayerStats>();
        if (!Enabled) return list;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name, wins, losses, kills, deaths, duels FROM duel_stats ORDER BY wins DESC, kills DESC LIMIT @c;";
        cmd.Parameters.AddWithValue("@c", count);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r));
        return list;
    }

    public async Task<(string Prim, string Sec)?> GetPrefs(ulong steamId)
    {
        if (!Enabled) return null;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT prim, sec FROM zw_duel_prefs WHERE steamid=@s;";
        cmd.Parameters.AddWithValue("@s", steamId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.GetString(1));
    }

    public async Task SavePrefs(ulong steamId, string prim, string sec)
    {
        if (!Enabled) return;
        await using var c = await Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO zw_duel_prefs (steamid, prim, sec) VALUES (@s, @p, @c) ON DUPLICATE KEY UPDATE prim=@p, sec=@c;";
        cmd.Parameters.AddWithValue("@s", steamId);
        cmd.Parameters.AddWithValue("@p", prim);
        cmd.Parameters.AddWithValue("@c", sec);
        await cmd.ExecuteNonQueryAsync();
    }

    private static PlayerStats Read(MySqlDataReader r) => new()
    {
        Name = r.GetString(0),
        Wins = r.GetInt32(1),
        Losses = r.GetInt32(2),
        Kills = r.GetInt32(3),
        Deaths = r.GetInt32(4),
        Duels = r.GetInt32(5),
    };

    private static string Trim(string s) => s.Length > 64 ? s[..64] : s;
}
