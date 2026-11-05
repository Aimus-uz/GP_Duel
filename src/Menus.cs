using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace GPDuel;

public partial class GPDuel
{
    private sealed class Rematch
    {
        public Duel Duel = null!;
        public HashSet<ulong> Yes = [];
        public DateTime Expires;
    }

    private readonly Dictionary<string, Rematch> _rematch = [];

    private void Open(CCSPlayerController p, CenterHtmlMenu menu) => MenuManager.OpenCenterHtmlMenu(this, p, menu);

    private static void Close(CCSPlayerController p)
    {
        if (p.IsValid) MenuManager.CloseActiveMenu(p);
    }

    // ---------------------------------------------------------------- main menu

    private void OpenMainMenu(CCSPlayerController p)
    {
        var menu = new CenterHtmlMenu("<font color='#ff3333'>DUEL</font>", this);
        var (prim, sec) = PrefsOf(p);
        int invites = _invites.Count(i => i.To.Slot == p.Slot);
        bool inCustom = _duelOf.TryGetValue(p.Slot, out var d) && d.Custom;

        if (inCustom)
            menu.AddMenuOption("<font color='#ff5555'>Duelni bekor qilish</font>", (pl, _) => { Close(pl); CancelDuel(pl); });
        else
            menu.AddMenuOption("Duelga chaqirish", (pl, _) => OpenPlayersMenu(pl));

        menu.AddMenuOption($"Takliflar [{invites}]", (pl, _) => OpenInvitesMenu(pl), invites == 0);
        menu.AddMenuOption($"Asosiy qurol: {WeaponLabel(Config.PrimaryWeapons, prim)}", (pl, _) => OpenPrefMenu(pl, primary: true));
        menu.AddMenuOption($"To'pponcha: {WeaponLabel(Config.SecondaryWeapons, sec)}", (pl, _) => OpenPrefMenu(pl, primary: false));
        menu.AddMenuOption("Statistika", (pl, _) => { Close(pl); ShowStats(pl); });
        menu.AddMenuOption("TOP 10", (pl, _) => { Close(pl); ShowTop(pl); });
        Open(p, menu);
    }

    private static string WeaponLabel(List<WeaponEntry> list, string weapon) =>
        list.FirstOrDefault(w => w.Weapon == weapon)?.Name ?? weapon.Replace("weapon_", "");

    private void OpenPrefMenu(CCSPlayerController p, bool primary)
    {
        var menu = new CenterHtmlMenu(primary ? "Asosiy qurol" : "To'pponcha", this);
        foreach (var w in primary ? Config.PrimaryWeapons : Config.SecondaryWeapons)
        {
            var entry = w;
            menu.AddMenuOption(entry.Name, (pl, _) =>
            {
                if (primary) SetPrefs(pl, prim: entry.Weapon); else SetPrefs(pl, sec: entry.Weapon);
                Chat(pl, $"{(primary ? "Asosiy qurol" : "To'pponcha")}: {{lime}}{entry.Name}{{default}} (keyingi raunddan)");
                OpenMainMenu(pl);
            });
        }
        Open(p, menu);
    }

    // ---------------------------------------------------------------- challenge: player -> mode -> wins

    private void OpenPlayersMenu(CCSPlayerController p)
    {
        var menu = new CenterHtmlMenu("DUEL | Raqibni tanlang", this);
        var list = Players().Where(o => o.Slot != p.Slot && InGame(o) && (!o.IsBot || Config.AllowBotChallenge)).ToList();
        if (list.Count == 0)
        {
            menu.AddMenuOption("O'yinchilar yo'q", (_, _) => { }, true);
        }
        foreach (var o in list)
        {
            var target = o;
            bool busy = _duelOf.TryGetValue(o.Slot, out var d) && d.Custom;
            var label = Html(o.PlayerName) + (o.IsBot ? " [BOT]" : "") + (busy ? " <font color='#888888'>(duelda)</font>" : "");
            menu.AddMenuOption(label, (pl, _) => OpenModeMenu(pl, target), busy);
        }
        Open(p, menu);
    }

    private void OpenModeMenu(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CenterHtmlMenu("DUEL | Rejimni tanlang", this);
        foreach (var mode in new[] { DuelMode.Normal, DuelMode.Awp, DuelMode.Pistol, DuelMode.Deagle, DuelMode.Knife })
        {
            var m = mode;
            menu.AddMenuOption(ModeName(m, ""), (pl, _) => OpenWinsMenu(pl, target, m, null, ""));
        }
        menu.AddMenuOption("Qurol tanlash...", (pl, _) => OpenWeaponMenu(pl, target), Config.ChallengeWeapons.Count == 0);
        Open(p, menu);
    }

    private void OpenWeaponMenu(CCSPlayerController p, CCSPlayerController target)
    {
        var menu = new CenterHtmlMenu("DUEL | Qurol tanlash", this);
        foreach (var w in Config.ChallengeWeapons)
        {
            var entry = w;
            menu.AddMenuOption(entry.Name, (pl, _) => OpenWinsMenu(pl, target, DuelMode.Weapon, entry.Weapon, entry.Name));
        }
        Open(p, menu);
    }

    private void OpenWinsMenu(CCSPlayerController p, CCSPlayerController target, DuelMode mode, string? weapon, string weaponName)
    {
        var menu = new CenterHtmlMenu("DUEL | G'alaba uchun raundlar", this);
        foreach (var n in Config.WinOptions)
        {
            var wins = n;
            menu.AddMenuOption(TargetText(wins), (pl, _) =>
            {
                Close(pl);
                SendInvite(pl, target, mode, weapon, weaponName, wins);
            });
        }
        Open(p, menu);
    }

    private void SendInvite(CCSPlayerController from, CCSPlayerController to, DuelMode mode, string? weapon, string weaponName, int target)
    {
        if (!to.IsValid || !InGame(to)) { Chat(from, "O'yinchi topilmadi."); return; }
        if (!InGame(from)) { Chat(from, "Avval jamoaga qo'shiling."); return; }
        if ((_duelOf.TryGetValue(from.Slot, out var d1) && d1.Custom) || (_duelOf.TryGetValue(to.Slot, out var d2) && d2.Custom))
        {
            Chat(from, "Kimdir allaqachon duelda.");
            return;
        }

        _invites.RemoveAll(i => i.From.Slot == from.Slot && i.To.Slot == to.Slot);
        var inv = new Invite
        {
            From = from, To = to, Mode = mode, Weapon = weapon, WeaponName = weaponName, Target = target,
            Expires = DateTime.UtcNow.AddSeconds(Math.Max(5, Config.InviteTimeoutSeconds)),
        };

        var conditions = $"{ModeName(mode, weaponName)}, {TargetText(target)}";

        if (to.IsBot)
        {
            Chat(from, $"{{gold}}{to.PlayerName}{{default}} (bot) qabul qildi!");
            StartCustom(from, to, mode, weapon, weaponName, target);
            return;
        }

        _invites.Add(inv);
        Chat(from, $"Chaqiruv {{gold}}{to.PlayerName}{{default}} ga yuborildi: {{lime}}{conditions}{{default}}.");
        Chat(to, $"{{gold}}{from.PlayerName}{{default}} sizni duelga chaqirdi: {{lime}}{conditions}{{default}}. Javob: {{lime}}!duel{{default}} → Takliflar");
        OpenAcceptMenu(to, inv);
    }

    // ---------------------------------------------------------------- invites

    private void OpenInvitesMenu(CCSPlayerController p)
    {
        var menu = new CenterHtmlMenu("Takliflar", this);
        foreach (var inv in _invites.Where(i => i.To.Slot == p.Slot && i.From.IsValid).ToList())
        {
            var i = inv;
            menu.AddMenuOption($"{Html(i.From.PlayerName)}: {Html(ModeName(i.Mode, i.WeaponName))}, {TargetText(i.Target)}", (pl, _) => OpenAcceptMenu(pl, i));
        }
        Open(p, menu);
    }

    private void OpenAcceptMenu(CCSPlayerController p, Invite inv)
    {
        var title = $"{Html(inv.From.PlayerName)} dan duel<br><font color='#cccccc'>{Html(ModeName(inv.Mode, inv.WeaponName))}, {TargetText(inv.Target)}</font>";
        var menu = new CenterHtmlMenu(title, this);
        menu.AddMenuOption("<font color='#55ff55'>Qabul qilish</font>", (pl, _) => { Close(pl); AcceptInvite(pl, inv); });
        menu.AddMenuOption("<font color='#ff5555'>Rad etish</font>", (pl, _) => { Close(pl); DeclineInvite(pl, inv); });
        Open(p, menu);
    }

    private void AcceptInvite(CCSPlayerController p, Invite inv)
    {
        if (!_invites.Contains(inv) || inv.Expires < DateTime.UtcNow)
        {
            _invites.Remove(inv);
            Chat(p, "Taklif muddati tugadi.");
            return;
        }
        _invites.RemoveAll(i => i.From.Slot == p.Slot || i.To.Slot == p.Slot || i.From.Slot == inv.From.Slot || i.To.Slot == inv.From.Slot);

        var from = inv.From;
        if (!from.IsValid || !InGame(from) || !InGame(p)) { Chat(p, "O'yinchi topilmadi."); return; }

        Chat(from, $"{{gold}}{p.PlayerName}{{default}} chaqiruvni qabul qildi!");
        StartCustom(from, p, inv.Mode, inv.Weapon, inv.WeaponName, inv.Target);
    }

    private void DeclineInvite(CCSPlayerController p, Invite inv)
    {
        _invites.Remove(inv);
        Chat(p, "Chaqiruv rad etildi.");
        Chat(inv.From, $"{{gold}}{p.PlayerName}{{default}} chaqiruvni rad etdi.");
    }

    // ---------------------------------------------------------------- rematch

    private void AskRematch(Duel duel)
    {
        if (!duel.A.IsValid || !duel.B.IsValid || duel.A.IsBot || duel.B.IsBot) return;
        var key = $"{Math.Min(duel.SidA, duel.SidB)}_{Math.Max(duel.SidA, duel.SidB)}";
        _rematch[key] = new Rematch { Duel = duel, Expires = DateTime.UtcNow.AddSeconds(Math.Max(5, Config.RematchTimeoutSeconds)) };

        foreach (var p in new[] { duel.A, duel.B })
        {
            var menu = new CenterHtmlMenu("Duelni takrorlash?", this);
            menu.AddMenuOption("<font color='#55ff55'>Ha, xuddi shunday</font>", (pl, _) => { Close(pl); VoteRematch(pl, key); });
            menu.AddMenuOption("Yo'q", (pl, _) =>
            {
                Close(pl);
                if (_rematch.Remove(key))
                    Chat(duel.Other(pl), $"{{gold}}{pl.PlayerName}{{default}} qayta o'ynashni xohlamadi.");
            });
            Open(p, menu);
        }
    }

    private void VoteRematch(CCSPlayerController p, string key)
    {
        if (!_rematch.TryGetValue(key, out var r) || r.Expires < DateTime.UtcNow) { Chat(p, "Vaqt tugadi."); return; }
        r.Yes.Add(p.SteamID);
        var other = r.Duel.Other(p);
        if (r.Yes.Count < 2)
        {
            Chat(p, "Raqib javobi kutilmoqda...");
            Chat(other, $"{{gold}}{p.PlayerName}{{default}} qayta o'ynashni xohlaydi: {{lime}}!duel{{default}} emas, menyuda «Ha» ni tanlang.");
            return;
        }
        _rematch.Remove(key);
        if (!other.IsValid || !InGame(other) || !InGame(p)) return;
        StartCustom(r.Duel.A, r.Duel.B, r.Duel.Mode, r.Duel.Weapon, r.Duel.WeaponName, r.Duel.Target);
    }

    // ---------------------------------------------------------------- arena editor (admin)

    private sealed class EditorState
    {
        public List<Arena> Arenas = [];
        public int Current = -1;
        public SpawnPair? Pending;
        public bool HasCT, HasT;
    }

    private readonly Dictionary<int, EditorState> _editor = [];

    private bool IsAdmin(CCSPlayerController? p) =>
        p != null && p.IsValid && AdminManager.PlayerHasPermissions(p, Config.AdminFlag);

    private EditorState Editor(CCSPlayerController p)
    {
        if (!_editor.TryGetValue(p.Slot, out var e))
        {
            // starts from the arenas already loaded for this map
            e = new EditorState
            {
                Arenas = _arenas.Select(a => new Arena { Id = a.Id, Pairs = [.. a.Pairs] }).ToList(),
            };
            e.Current = e.Arenas.Count - 1;
            _editor[p.Slot] = e;
        }
        return e;
    }

    private static (float[] pos, float[] ang)? Here(CCSPlayerController p)
    {
        var pawn = p.PlayerPawn.Value;
        if (pawn?.AbsOrigin == null) return null;
        var o = pawn.AbsOrigin;
        var a = pawn.EyeAngles;
        return (new[] { o.X, o.Y, o.Z }, new[] { a.X, a.Y, 0f });
    }

    [ConsoleCommand("css_arena", "Arena editor: info")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaInfo(CCSPlayerController? p, CommandInfo cmd)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        Chat(p, $"Karta: {{gold}}{Server.MapName}{{default}} | arenalar: {e.Arenas.Count} | hozirgi: {e.Current} ({(e.Current >= 0 && e.Current < e.Arenas.Count ? e.Arenas[e.Current].Pairs.Count : 0)} juft)");
        Chat(p, "!arena_new — yangi arena | !arena_ct / !arena_t — juft nuqtalari | !arena_del <raqam> | !arena_tp <raqam> | !arena_save");
    }

    [ConsoleCommand("css_arena_new", "Arena editor: new arena")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaNew(CCSPlayerController? p, CommandInfo cmd)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        e.Arenas.Add(new Arena { Id = e.Arenas.Count });
        e.Current = e.Arenas.Count - 1;
        e.Pending = null; e.HasCT = e.HasT = false;
        Chat(p, $"Yangi arena {{gold}}{e.Current}{{default}}. Endi !arena_ct va !arena_t bilan nuqtalarni belgilang.");
    }

    [ConsoleCommand("css_arena_ct", "Arena editor: CT point of a pair")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaCT(CCSPlayerController? p, CommandInfo cmd) => EditorPoint(p, ct: true);

    [ConsoleCommand("css_arena_t", "Arena editor: T point of a pair")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaT(CCSPlayerController? p, CommandInfo cmd) => EditorPoint(p, ct: false);

    private void EditorPoint(CCSPlayerController? p, bool ct)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        if (e.Current < 0 || e.Current >= e.Arenas.Count) { Chat(p, "Avval !arena_new."); return; }
        var here = Here(p!);
        if (here == null) { Chat(p, "Tirik bo'ling."); return; }

        e.Pending ??= new SpawnPair();
        if (ct) { e.Pending.CTPos = here.Value.pos; e.Pending.CTAng = here.Value.ang; e.HasCT = true; }
        else { e.Pending.TPos = here.Value.pos; e.Pending.TAng = here.Value.ang; e.HasT = true; }

        var pp = here.Value.pos;
        Chat(p, $"{(ct ? "CT" : "T")} nuqtasi: {pp[0]:F0} {pp[1]:F0} {pp[2]:F0}");

        if (e.HasCT && e.HasT)
        {
            e.Arenas[e.Current].Pairs.Add(e.Pending);
            Chat(p, $"Arena {e.Current}: juft {{lime}}{e.Arenas[e.Current].Pairs.Count}{{default}} qo'shildi. Yana juft yoki !arena_save.");
            e.Pending = null; e.HasCT = e.HasT = false;
        }
    }

    [ConsoleCommand("css_arena_del", "Arena editor: delete arena <number>")]
    [CommandHelper(minArgs: 1, usage: "<number>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaDel(CCSPlayerController? p, CommandInfo cmd)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        if (!int.TryParse(cmd.GetArg(1), out int id) || id < 0 || id >= e.Arenas.Count) { Chat(p, "Noto'g'ri raqam."); return; }
        e.Arenas.RemoveAt(id);
        e.Current = e.Arenas.Count - 1;
        Chat(p, $"Arena {id} o'chirildi (!arena_save bilan saqlang). Qolgan: {e.Arenas.Count}");
    }

    [ConsoleCommand("css_arena_tp", "Arena editor: teleport to arena <number>")]
    [CommandHelper(minArgs: 1, usage: "<number>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaTp(CCSPlayerController? p, CommandInfo cmd)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        if (!int.TryParse(cmd.GetArg(1), out int id) || id < 0 || id >= e.Arenas.Count || e.Arenas[id].Pairs.Count == 0) { Chat(p, "Noto'g'ri raqam."); return; }
        var pair = e.Arenas[id].Pairs[0];
        Teleport(p!, pair.CTPos, pair.CTAng);
        Chat(p, $"Arena {id}, CT nuqtasi.");
    }

    [ConsoleCommand("css_arena_save", "Arena editor: save spawns/<map>.txt")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnArenaSave(CCSPlayerController? p, CommandInfo cmd)
    {
        if (!IsAdmin(p)) return;
        var e = Editor(p!);
        var arenas = e.Arenas.Where(a => a.Pairs.Count > 0).ToList();
        try
        {
            ArenaFile.Save(SpawnFile, arenas);
        }
        catch (Exception ex)
        {
            Chat(p, $"{{lightred}}Saqlab bo'lmadi:{{default}} {ex.Message}");
            return;
        }
        _editor.Remove(p!.Slot);

        // all duels restart on the new arenas
        foreach (var d in _duelOf.Values.Distinct().ToList()) EndDuel(d, null, matchmake: false);
        foreach (var slot in _soloOf.Keys.ToList())
        {
            var pl = Utilities.GetPlayerFromSlot(slot);
            if (pl != null) ReleaseSolo(pl);
        }
        _soloOf.Clear();
        _soloSince.Clear();
        LoadArenas();
        Chat(p, $"Saqlandi: {{lime}}{arenas.Count}{{default}} arena → spawns/{Server.MapName}.txt");
        Server.NextFrame(Matchmake);
    }
}
