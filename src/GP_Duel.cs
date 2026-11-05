using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace GPDuel;

public enum DuelMode { Normal, Awp, Pistol, Deagle, Knife, Weapon }

public sealed class Duel
{
    public CCSPlayerController A = null!, B = null!;
    public ulong SidA, SidB;
    public Arena Arena = null!;
    public bool Custom;
    public DuelMode Mode;
    public string? Weapon;      // DuelMode.Weapon
    public string WeaponName = "";
    public int Target;          // wins needed, 0 = no limit
    public int ScoreA, ScoreB;
    public int PairIndex;
    public bool Swap;           // A on the T side
    public bool Ended;

    public CCSPlayerController Other(CCSPlayerController p) => p.Slot == A.Slot ? B : A;
    public int ScoreOf(CCSPlayerController p) => p.Slot == A.Slot ? ScoreA : ScoreB;
    public bool Has(CCSPlayerController p) => p.Slot == A.Slot || p.Slot == B.Slot;
}

public sealed class Invite
{
    public CCSPlayerController From = null!, To = null!;
    public DuelMode Mode;
    public string? Weapon;
    public string WeaponName = "";
    public int Target;
    public DateTime Expires;
}

public partial class GPDuel : BasePlugin, IPluginConfig<GPDuelConfig>
{
    public override string ModuleName => "GP Duel";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "GAMEPOINT";
    public override string ModuleDescription => "1v1 arenas: random duels + duels from the menu (!duel)";

    public GPDuelConfig Config { get; set; } = new();
    private Database _db = null!;
    private readonly Random _rng = new();

    private List<Arena> _arenas = [];
    private readonly Dictionary<int, Duel> _duelOf = [];        // slot -> duel
    private readonly Dictionary<int, Arena> _soloOf = [];       // slot -> arena of a player without opponent
    private readonly Dictionary<int, DateTime> _soloSince = [];
    private readonly Dictionary<ulong, ulong> _lastOpponent = [];
    private readonly List<Invite> _invites = [];
    private readonly HashSet<int> _warnedNoArenas = [];
    private readonly Dictionary<ulong, (string Prim, string Sec)> _prefs = [];
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;

    public void OnConfigParsed(GPDuelConfig config)
    {
        Config = config;
        _db = new Database(config.Database);
    }

    public override void Load(bool hotReload)
    {
        _db ??= new Database(Config.Database);

        if (_db.Enabled)
            Task.Run(async () =>
            {
                try { await _db.Init(); }
                catch (Exception e) { Logger.LogError("[GP Duel] database: {Error}", e.Message); }
            });

        RegisterListener<Listeners.OnMapStart>(_ => Server.NextFrame(OnMapStart));
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        _hudTimer = AddTimer(0.5f, OnHud, TimerFlags.REPEAT);
        AddTimer(3.0f, OnMaintenance, TimerFlags.REPEAT);

        if (hotReload)
            Server.NextFrame(() =>
            {
                OnMapStart();
                foreach (var p in Players()) LoadPrefs(p);
            });
    }

    // ---------------------------------------------------------------- map

    private string SpawnFile => Path.Combine(ModuleDirectory, "spawns", $"{Server.MapName}.txt");

    private void OnMapStart()
    {
        _duelOf.Clear();
        _soloOf.Clear();
        _soloSince.Clear();
        _invites.Clear();
        _rematch.Clear();
        _editor.Clear();
        _warnedNoArenas.Clear();

        LoadArenas();
        AddTimer(1.0f, () =>
        {
            foreach (var cmd in Config.ServerCommands) Server.ExecuteCommand(cmd);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void LoadArenas()
    {
        try { _arenas = ArenaFile.Load(SpawnFile); }
        catch (Exception e)
        {
            _arenas = [];
            Logger.LogError("[GP Duel] {File}: {Error}", SpawnFile, e.Message);
        }
        Logger.LogInformation("[GP Duel] map {Map}: {Count} arenas", Server.MapName, _arenas.Count);
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<CCSPlayerController> Players() =>
        Utilities.GetPlayers().Where(p => p is { IsValid: true, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected);

    private static bool InGame(CCSPlayerController? p) =>
        p is { IsValid: true } && (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist);

    private static bool Alive(CCSPlayerController p) =>
        p.PawnIsAlive && p.PlayerPawn.Value is { IsValid: true };

    private string Prefix => " " + Colors(Config.ChatPrefix);

    private void Chat(CCSPlayerController? p, string msg)
    {
        if (p is { IsValid: true, IsBot: false }) p.PrintToChat($"{Prefix} {Colors(msg)}");
    }

    private void ChatAll(string msg) => Server.PrintToChatAll($"{Prefix} {Colors(msg)}");

    private static string Colors(string s) => s
        .Replace("{default}", ChatColors.Default.ToString())
        .Replace("{white}", ChatColors.White.ToString())
        .Replace("{red}", ChatColors.Red.ToString())
        .Replace("{lightred}", ChatColors.LightRed.ToString())
        .Replace("{green}", ChatColors.Green.ToString())
        .Replace("{lime}", ChatColors.Lime.ToString())
        .Replace("{yellow}", ChatColors.Yellow.ToString())
        .Replace("{gold}", ChatColors.Gold.ToString())
        .Replace("{blue}", ChatColors.Blue.ToString())
        .Replace("{grey}", ChatColors.Grey.ToString());

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private Arena? FreeArena()
    {
        var free = _arenas.Where(a => a.Owner == null).ToList();
        if (free.Count == 0) return null;
        return Config.RandomizeArena ? free[_rng.Next(free.Count)] : free[0];
    }

    private string ModeName(DuelMode mode, string weaponName) => mode switch
    {
        DuelMode.Awp => "Faqat AWP",
        DuelMode.Pistol => "Faqat to'pponcha",
        DuelMode.Deagle => "Faqat Desert Eagle",
        DuelMode.Knife => "Faqat pichoq",
        DuelMode.Weapon => weaponName,
        _ => "Oddiy",
    };

    private static string TargetText(int target) => target <= 0 ? "Cheksiz" : $"{target} g'alabagacha";

    private static DuelMode ParseMode(string s) => s.Trim().ToLowerInvariant() switch
    {
        "awp" => DuelMode.Awp,
        "pistol" => DuelMode.Pistol,
        "deagle" => DuelMode.Deagle,
        "knife" => DuelMode.Knife,
        _ => DuelMode.Normal,
    };

    // ---------------------------------------------------------------- matchmaking

    // Every player in a team without a duel gets a random opponent; a lone player waits in a free arena
    private void Matchmake()
    {
        var free = Players().Where(p => InGame(p) && !_duelOf.ContainsKey(p.Slot)).OrderBy(_ => _rng.Next()).ToList();

        while (free.Count >= 2)
        {
            var a = free[0];
            _lastOpponent.TryGetValue(a.SteamID, out var last);
            var b = free.Skip(1).FirstOrDefault(p => p.SteamID != last || p.IsBot) ?? free[1];
            free.Remove(a);
            free.Remove(b);
            if (!StartCasual(a, b))
            {
                free.Insert(0, a);
                free.Insert(1, b);
                break;
            }
        }

        foreach (var p in free) EnsureSolo(p);
    }

    private void EnsureSolo(CCSPlayerController p)
    {
        if (_soloOf.ContainsKey(p.Slot) || _duelOf.ContainsKey(p.Slot)) return;

        var arena = FreeArena();
        if (arena == null)
        {
            if (_arenas.Count == 0)
            {
                if (_warnedNoArenas.Add(p.Slot))
                    Chat(p, "{lightred}Bu kartada arena yo'q.{default} Admin: !arena_new bilan arena yarating.");
            }
            else
            {
                Chat(p, "{lightred}Bo'sh arena yo'q,{default} tomoshabinlarga o'tkazildingiz. Keyinroq qayta kiring.");
                p.ChangeTeam(CsTeam.Spectator);
            }
            return;
        }

        arena.Owner = p;
        _soloOf[p.Slot] = arena;
        _soloSince[p.Slot] = DateTime.UtcNow;
        if (Alive(p)) PlaceSolo(p, arena);
    }

    private void ReleaseSolo(CCSPlayerController p)
    {
        if (_soloOf.Remove(p.Slot, out var arena) && arena.Owner is CCSPlayerController owner && owner.Slot == p.Slot) arena.Owner = null;
        _soloSince.Remove(p.Slot);
    }

    private bool StartCasual(CCSPlayerController a, CCSPlayerController b)
    {
        _soloOf.TryGetValue(a.Slot, out var arenaA);
        _soloOf.TryGetValue(b.Slot, out var arenaB);
        ReleaseSolo(a);
        ReleaseSolo(b);

        var arena = arenaA ?? arenaB ?? FreeArena();
        if (arena == null) return false;

        List<string> modes = Config.CasualModes.Count > 0 ? Config.CasualModes : ["normal"];
        var duel = new Duel
        {
            A = a, B = b, SidA = a.SteamID, SidB = b.SteamID,
            Arena = arena,
            Custom = false,
            Mode = ParseMode(modes[_rng.Next(modes.Count)]),
            Target = Math.Max(1, Config.CasualKillsToWin),
        };
        Begin(duel);

        var text = $"{{lime}}{ModeName(duel.Mode, "")}{{default}}, {duel.Target} o'ldirishgacha";
        Chat(a, $"Raqib: {{gold}}{b.PlayerName}{{default}} | {text}");
        Chat(b, $"Raqib: {{gold}}{a.PlayerName}{{default}} | {text}");
        return true;
    }

    private bool StartCustom(CCSPlayerController a, CCSPlayerController b, DuelMode mode, string? weapon, string weaponName, int target)
    {
        // the arena of their current random duels can be reused
        Arena? arena = null;
        foreach (var p in new[] { a, b })
        {
            if (_duelOf.TryGetValue(p.Slot, out var old))
            {
                if (old.Custom) return false;
                arena ??= old.Arena;
                var partner = old.Other(p);
                if (partner.Slot != a.Slot && partner.Slot != b.Slot) Chat(partner, "Raqibingiz duelga chaqirildi, yangi raqib qidirilmoqda...");
                EndDuel(old, null, matchmake: false, reuseArena: old.Arena == arena);
            }
        }
        _soloOf.TryGetValue(a.Slot, out var sa);
        _soloOf.TryGetValue(b.Slot, out var sb);
        ReleaseSolo(a);
        ReleaseSolo(b);
        arena ??= sa ?? sb ?? FreeArena();

        if (arena == null)
        {
            Chat(a, "{lightred}Bo'sh arena yo'q,{default} keyinroq urinib ko'ring.");
            Chat(b, "{lightred}Bo'sh arena yo'q,{default} keyinroq urinib ko'ring.");
            Server.NextFrame(Matchmake);
            return false;
        }

        var duel = new Duel
        {
            A = a, B = b, SidA = a.SteamID, SidB = b.SteamID,
            Arena = arena,
            Custom = true,
            Mode = mode, Weapon = weapon, WeaponName = weaponName,
            Target = target,
        };
        Begin(duel);

        ChatAll($"{{gold}}{a.PlayerName}{{default}} vs {{gold}}{b.PlayerName}{{default}} — {{lime}}{ModeName(mode, weaponName)}{{default}}, {TargetText(target)}!");
        Server.NextFrame(Matchmake); // partners of the ended random duels get new opponents
        return true;
    }

    private void Begin(Duel duel)
    {
        duel.Arena.Owner = duel;
        _duelOf[duel.A.Slot] = duel;
        _duelOf[duel.B.Slot] = duel;
        _lastOpponent[duel.SidA] = duel.SidB;
        _lastOpponent[duel.SidB] = duel.SidA;
        NewRound(duel);
    }

    // winner == null: duel cancelled (left, cancel, team change) - no wins/losses
    private void EndDuel(Duel duel, CCSPlayerController? winner, bool matchmake = true, bool reuseArena = false)
    {
        if (duel.Ended) return;
        duel.Ended = true;

        if (_duelOf.TryGetValue(duel.A.Slot, out var da) && da == duel) _duelOf.Remove(duel.A.Slot);
        if (_duelOf.TryGetValue(duel.B.Slot, out var db) && db == duel) _duelOf.Remove(duel.B.Slot);
        if (!reuseArena && duel.Arena.Owner == duel) duel.Arena.Owner = null;
        if (reuseArena) duel.Arena.Owner = null; // taken again right away by the caller

        if (duel.Custom && winner != null && winner.IsValid)
        {
            var loser = duel.Other(winner);
            ChatAll($"G'olib: {{lime}}{winner.PlayerName}{{default}} ({duel.ScoreOf(winner)} : {duel.ScoreOf(loser)}) — {{gold}}{loser.PlayerName}{{default}} ustidan, {ModeName(duel.Mode, duel.WeaponName)}");
            AddStats(winner, wins: 1, duels: 1);
            if (loser.IsValid) AddStats(loser, losses: 1, duels: 1);
            AskRematch(duel);
        }

        if (matchmake) Server.NextFrame(Matchmake);
    }

    // ---------------------------------------------------------------- rounds

    private void NewRound(Duel duel)
    {
        duel.PairIndex = _rng.Next(duel.Arena.Pairs.Count);
        if (Config.SwapSidesEachRound) duel.Swap = _rng.Next(2) == 0;

        foreach (var p in new[] { duel.A, duel.B })
        {
            if (!p.IsValid) continue;
            if (Alive(p)) Place(p, duel);
            else if (InGame(p)) p.Respawn(); // OnPlayerSpawn places him
        }
    }

    private void Place(CCSPlayerController p, Duel duel)
    {
        var pair = duel.Arena.Pairs[Math.Clamp(duel.PairIndex, 0, duel.Arena.Pairs.Count - 1)];
        bool ct = (p.Slot == duel.A.Slot) != duel.Swap;
        Teleport(p, ct ? pair.CTPos : pair.TPos, ct ? pair.CTAng : pair.TAng);
        Equip(p, duel.Mode, duel.Weapon);
    }

    private void PlaceSolo(CCSPlayerController p, Arena arena)
    {
        var pair = arena.Pairs[_rng.Next(arena.Pairs.Count)];
        Teleport(p, pair.CTPos, pair.CTAng);
        Equip(p, DuelMode.Normal, null);
    }

    private static void Teleport(CCSPlayerController p, float[] pos, float[] ang)
    {
        var pawn = p.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        pawn.Teleport(new Vector(pos[0], pos[1], pos[2] + 2f), new QAngle(ang[0], ang[1], 0f), new Vector(0f, 0f, 0f));
    }

    private void Equip(CCSPlayerController p, DuelMode mode, string? weapon)
    {
        var pawn = p.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.Health = 100;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

        p.RemoveWeapons();
        p.GiveNamedItem("weapon_knife");

        var (prim, sec) = PrefsOf(p);
        string? primary = null, secondary = null;
        switch (mode)
        {
            case DuelMode.Normal: primary = prim; secondary = sec; break;
            case DuelMode.Awp: primary = "weapon_awp"; break;
            case DuelMode.Pistol: secondary = sec; break;
            case DuelMode.Deagle: secondary = "weapon_deagle"; break;
            case DuelMode.Weapon: primary = weapon; break;
            case DuelMode.Knife: break;
        }
        if (!string.IsNullOrEmpty(secondary)) p.GiveNamedItem(secondary);
        if (!string.IsNullOrEmpty(primary)) p.GiveNamedItem(primary);
        if (Config.GiveArmor) p.GiveNamedItem(Config.GiveHelmet ? "item_assaultsuit" : "item_kevlar");

        var slot = !string.IsNullOrEmpty(primary) ? "slot1" : !string.IsNullOrEmpty(secondary) ? "slot2" : "slot3";
        Server.NextFrame(() => { if (p.IsValid && !p.IsBot) p.ExecuteClientCommand(slot); });
    }

    // ---------------------------------------------------------------- events

    private HookResult OnPlayerSpawn(EventPlayerSpawn ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p == null || !p.IsValid) return HookResult.Continue;

        Server.NextFrame(() =>
        {
            if (!p.IsValid || !InGame(p) || !Alive(p)) return;
            if (_duelOf.TryGetValue(p.Slot, out var duel)) Place(p, duel);
            else if (_soloOf.TryGetValue(p.Slot, out var arena)) PlaceSolo(p, arena);
            else Matchmake();
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath ev, GameEventInfo info)
    {
        var victim = ev.Userid;
        if (victim == null || !victim.IsValid) return HookResult.Continue;
        var attacker = ev.Attacker;

        if (_duelOf.TryGetValue(victim.Slot, out var duel))
        {
            var opponent = duel.Other(victim);
            bool byOpponent = attacker != null && attacker.IsValid && attacker.Slot == opponent.Slot;
            bool selfOrWorld = attacker == null || !attacker.IsValid || attacker.Slot == victim.Slot;

            if (byOpponent || selfOrWorld)
            {
                if (opponent.Slot == duel.A.Slot) duel.ScoreA++; else duel.ScoreB++;
                if (byOpponent)
                {
                    AddStats(opponent, kills: 1);
                    AddStats(victim, deaths: 1);
                }
            }

            if (duel.Target > 0 && duel.ScoreOf(opponent) >= duel.Target)
            {
                EndDuel(duel, opponent);
                return HookResult.Continue;
            }

            AddTimer(Config.RoundDelaySeconds, () =>
            {
                if (duel.Ended) return;
                NewRound(duel);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        else if (_soloOf.ContainsKey(victim.Slot))
        {
            AddTimer(Config.RoundDelaySeconds, () =>
            {
                if (victim.IsValid && InGame(victim) && !Alive(victim)) victim.Respawn();
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p == null || !p.IsValid || ev.Disconnect) return HookResult.Continue;

        // a change of the side inside the duel mode means nothing; only Spectator leaves the arenas
        if (ev.Team != (int)CsTeam.Terrorist && ev.Team != (int)CsTeam.CounterTerrorist)
            Leave(p, "tomoshabinga o'tdi");
        else
            Server.NextFrame(() =>
            {
                if (!p.IsValid) return;
                if (!_duelOf.ContainsKey(p.Slot) && !_soloOf.ContainsKey(p.Slot)) Matchmake();
                if (!Alive(p) && InGame(p)) p.Respawn();
            });
        return HookResult.Continue;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p != null && p.IsValid && !p.IsBot) LoadPrefs(p);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p != null && p.IsValid) Leave(p, "serverdan chiqdi");
        return HookResult.Continue;
    }

    private void Leave(CCSPlayerController p, string reason)
    {
        _invites.RemoveAll(i => i.From.Slot == p.Slot || i.To.Slot == p.Slot);
        ReleaseSolo(p);
        if (_duelOf.TryGetValue(p.Slot, out var duel))
        {
            var other = duel.Other(p);
            if (duel.Custom) Chat(other, $"Duel bekor qilindi: {{gold}}{p.PlayerName}{{default}} {reason}.");
            else Chat(other, "Raqib ketdi, yangi raqib qidirilmoqda...");
            EndDuel(duel, null);
        }
    }

    // Every 3 s: expired invites, lone players waiting too long
    private void OnMaintenance()
    {
        var now = DateTime.UtcNow;
        foreach (var inv in _invites.Where(i => i.Expires < now).ToList())
        {
            _invites.Remove(inv);
            Chat(inv.From, $"{{gold}}{(inv.To.IsValid ? inv.To.PlayerName : "?")}{{default}} chaqiruvga javob bermadi.");
        }

        foreach (var key in _rematch.Where(r => r.Value.Expires < now).Select(r => r.Key).ToList())
            _rematch.Remove(key);

        if (Config.SoloMaxWaitSeconds <= 0) return;

        // a lone player waits too long while random duels run: one random pair is split to give him an opponent
        var waiting = _soloOf.Keys.Where(s => _soloSince.TryGetValue(s, out var t) && (now - t).TotalSeconds > Config.SoloMaxWaitSeconds).ToList();
        if (waiting.Count == 0) return;
        var casual = _duelOf.Values.Distinct().Where(d => !d.Custom && !d.Ended).OrderBy(_ => _rng.Next()).FirstOrDefault();
        if (casual == null) return;

        Chat(casual.A, "Raqiblar almashtirildi.");
        Chat(casual.B, "Raqiblar almashtirildi.");
        EndDuel(casual, null);
    }

    // ---------------------------------------------------------------- HUD

    private void OnHud()
    {
        if (!Config.HudEnabled) return;
        foreach (var p in Players())
        {
            if (p.IsBot || !InGame(p)) continue;
            if (MenuManager.GetActiveMenu(p) != null) continue;

            string? html = null;
            if (_duelOf.TryGetValue(p.Slot, out var duel))
            {
                var o = duel.Other(p);
                var oname = Html(o.IsValid ? o.PlayerName : "?");
                if (duel.Custom || Config.CasualShowScore)
                {
                    var limit = duel.Target > 0 ? $"G'alaba kerak: {duel.Target}" : "Cheksiz";
                    html = $"<font class='fontSize-l' color='#ff3333'>{(duel.Custom ? "DUEL" : "RAQIB")}</font><br>" +
                           $"<font color='#00ff00'>{Html(p.PlayerName)}</font> <font class='fontSize-l' color='#ffffff'>[ {duel.ScoreOf(p)} : {duel.ScoreOf(o)} ]</font> <font color='#ff3333'>{oname}</font><br>" +
                           $"<font color='#cccccc'>{Html(ModeName(duel.Mode, duel.WeaponName))} | {limit}</font>";
                }
                else
                {
                    html = $"<font color='#cccccc'>Raqib:</font> <font class='fontSize-l' color='#ff3333'>{oname}</font><br>" +
                           $"<font color='#88ccff'>!duel — duel menyusi</font>";
                }
            }
            else if (_soloOf.ContainsKey(p.Slot))
            {
                html = "<font class='fontSize-l' color='#ffcc00'>RAQIB KUTILMOQDA</font><br><font color='#88ccff'>!duel — duel menyusi</font>";
            }

            if (html != null) p.PrintToCenterHtml(html, 1);
        }
    }

    // ---------------------------------------------------------------- stats / prefs

    private void AddStats(CCSPlayerController p, int wins = 0, int losses = 0, int kills = 0, int deaths = 0, int duels = 0)
    {
        if (!_db.Enabled || p.IsBot || !p.IsValid) return;
        ulong sid = p.SteamID;
        string name = p.PlayerName;
        Task.Run(async () =>
        {
            try { await _db.Add(sid, name, wins, losses, kills, deaths, duels); }
            catch (Exception e) { Logger.LogError("[GP Duel] stats: {Error}", e.Message); }
        });
    }

    private (string Prim, string Sec) PrefsOf(CCSPlayerController p) =>
        _prefs.TryGetValue(p.SteamID, out var v) ? v : (Config.DefaultPrimary, Config.DefaultSecondary);

    private void LoadPrefs(CCSPlayerController p)
    {
        if (!_db.Enabled || p.IsBot) return;
        ulong sid = p.SteamID;
        Task.Run(async () =>
        {
            try
            {
                var prefs = await _db.GetPrefs(sid);
                if (prefs != null) Server.NextFrame(() => _prefs[sid] = prefs.Value);
            }
            catch (Exception e) { Logger.LogError("[GP Duel] prefs: {Error}", e.Message); }
        });
    }

    private void SetPrefs(CCSPlayerController p, string? prim = null, string? sec = null)
    {
        var cur = PrefsOf(p);
        var next = (prim ?? cur.Prim, sec ?? cur.Sec);
        _prefs[p.SteamID] = next;
        if (!_db.Enabled || p.IsBot) return;
        ulong sid = p.SteamID;
        Task.Run(async () =>
        {
            try { await _db.SavePrefs(sid, next.Item1, next.Item2); }
            catch (Exception e) { Logger.LogError("[GP Duel] prefs: {Error}", e.Message); }
        });
    }

    // ---------------------------------------------------------------- commands

    [ConsoleCommand("css_duel", "Duel menu")]
    [ConsoleCommand("css_d", "Duel menu")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDuelCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        OpenMainMenu(player);
    }

    [ConsoleCommand("css_cancel", "Cancel the duel")]
    [ConsoleCommand("css_stop", "Cancel the duel")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCancelCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        CancelDuel(player);
    }

    [ConsoleCommand("css_rank", "Duel statistics")]
    [ConsoleCommand("css_duelstats", "Duel statistics")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRankCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        ShowStats(player);
    }

    [ConsoleCommand("css_top", "Top duel players")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        ShowTop(player);
    }

    private void CancelDuel(CCSPlayerController p)
    {
        if (!_duelOf.TryGetValue(p.Slot, out var duel) || !duel.Custom)
        {
            Chat(p, "Siz duelda emassiz.");
            return;
        }
        var other = duel.Other(p);
        Chat(p, "Duel bekor qilindi.");
        Chat(other, $"Duel bekor qilindi: {{gold}}{p.PlayerName}{{default}} bekor qildi.");
        EndDuel(duel, null);
    }

    private void ShowStats(CCSPlayerController p)
    {
        if (!_db.Enabled) { Chat(p, "Statistika o'chirilgan."); return; }
        ulong sid = p.SteamID;
        Task.Run(async () =>
        {
            PlayerStats? s = null;
            try { s = await _db.GetStats(sid); }
            catch (Exception e) { Logger.LogError("[GP Duel] stats: {Error}", e.Message); }
            Server.NextFrame(() =>
            {
                if (!p.IsValid) return;
                if (s == null) { Chat(p, "Statistika mavjud emas."); return; }
                var kd = s.Deaths == 0 ? s.Kills : (double)s.Kills / s.Deaths;
                Chat(p, $"G'alaba: {{lime}}{s.Wins}{{default}} | Mag'lubiyat: {{lightred}}{s.Losses}{{default}} | Duellar: {s.Duels}");
                Chat(p, $"O'ldirish: {{lime}}{s.Kills}{{default}} | O'lim: {{lightred}}{s.Deaths}{{default}} | K/D: {{gold}}{kd:0.00}");
            });
        });
    }

    private void ShowTop(CCSPlayerController p)
    {
        if (!_db.Enabled) { Chat(p, "Statistika o'chirilgan."); return; }
        Task.Run(async () =>
        {
            List<PlayerStats> top = [];
            try { top = await _db.GetTop(10); }
            catch (Exception e) { Logger.LogError("[GP Duel] top: {Error}", e.Message); }
            Server.NextFrame(() =>
            {
                if (!p.IsValid) return;
                if (top.Count == 0) { Chat(p, "Statistika mavjud emas."); return; }
                Chat(p, "{gold}TOP 10{default} (g'alabalar bo'yicha):");
                for (int i = 0; i < top.Count; i++)
                {
                    var s = top[i];
                    Chat(p, $"{{gold}}{i + 1}.{{default}} {s.Name} — {{lime}}{s.Wins}{{default}} G / {{lightred}}{s.Losses}{{default}} M | {s.Kills} o'ldirish");
                }
            });
        });
    }
}
