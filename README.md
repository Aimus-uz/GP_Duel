# GP Duel

Плагин дуэлей 1v1 для **CounterStrikeSharp** (CS2), сервер **GAMEPOINT.UZ**. Новая версия на основе идей ZaadrotDuel, написанная заново.

- На карте много маленьких арен, и все игроки одновременно дерутся 1 на 1.
- **Случайные дуэли:** каждый получает случайного соперника и играет до N убийств (по умолчанию 5), затем пары перемешиваются. Повторить того же соперника подряд плагин старается не давать.
- **Дуэль через меню `!duel`:** выбираешь игрока, режим или конкретное оружие и до скольких побед. Соперник получает меню **Qabul qilish / Rad etish**.
- **Счёт только у дуэлянтов:** в центре экрана `ник [ 3 : 1 ] ник`, сколько нужно побед и режим.
- **После дуэли** объявляется победитель и предлагается реванш **«Duelni takrorlash? Ha / Yo'q»**.
- **Своё оружие** для режимов «Oddiy» и «To'pponcha» (меню `!duel` → Asosiy qurol / To'pponcha). Выбор сохраняется в базе.
- **Статистика:** `!rank`, а также `!top`.
- **Редактор арен** для админа.
- **Всё настраивается в JSON-конфиге,** пароль базы тоже там, в коде его нет.

Работает со старыми таблицами ZaadrotDuel (`duel_stats`, `zw_duel_prefs`) и с его файлами спавнов `spawns/<карта>.txt`: статистика и арены сохраняются.

---

## Требования
- Metamod:Source
- CounterStrikeSharp **1.0.379** (.NET 10)

## Установка
1. Скачай артефакт **GP_Duel**: Actions → последняя зелёная сборка → Artifacts.
2. Скопируй содержимое в `game/csgo/`. Получится:
   ```
   addons/counterstrikesharp/plugins/GP_Duel/GP_Duel.dll
   addons/counterstrikesharp/plugins/GP_Duel/MySqlConnector.dll (и другие .dll из архива)
   addons/counterstrikesharp/plugins/GP_Duel/spawns/duels_aim_map.txt
   addons/counterstrikesharp/plugins/GP_Duel/spawns/duels_mirage_1x1.txt
   ```
3. **Удали старый ZaadrotDuel** (`plugins/ZaadrotDuel/`), иначе два плагина будут конфликтовать.
4. Запусти сервер один раз: появится конфиг `addons/counterstrikesharp/configs/plugins/GP_Duel/GP_Duel.json`.
5. Впиши в него данные базы (блок `Database`) и перезапусти сервер.

Слоты: на каждую арену 2 игрока. Например, в `duels_aim_map` 11 арен, это 22 игрока; в `duels_mirage_1x1` 9 арен, это 18 игроков. Ставь `-maxplayers` не больше этого числа, иначе лишние игроки будут уходить в наблюдатели.

---

## Команды игроков
| Команда | Что делает |
|---|---|
| `!duel` / `!d` | Меню: вызвать на дуэль, приглашения, своё оружие, статистика, топ |
| `!cancel` / `!stop` | Отменить свою дуэль (без побед и поражений) |
| `!rank` / `!duelstats` | Моя статистика |
| `!top` | Топ-10 по победам |

### Как вызвать
`!duel` → **Duelga chaqirish** → игрок → режим:
- **Oddiy** — у каждого своё основное оружие и пистолет;
- **Faqat AWP** / **Faqat to'pponcha** / **Faqat Desert Eagle** / **Faqat pichoq**;
- **Qurol tanlash...** — конкретное оружие из списка `ChallengeWeapons`;

→ до скольких побед (3 / 5 / 10 / 15 / Cheksiz) → соперник получает меню **Qabul qilish / Rad etish**.

Приглашение действует 30 секунд. Его можно принять и позже: `!duel` → **Takliflar**.

---

## Редактор арен (флаг `@css/root`)
| Команда | Что делает |
|---|---|
| `!arena` | Сколько арен на карте и подсказка по командам |
| `!arena_new` | Новая арена |
| `!arena_ct` | Встать на место первого игрока и написать: точка CT текущей пары |
| `!arena_t` | Точка T: пара добавляется в арену. Можно добавить несколько пар на одну арену |
| `!arena_tp <номер>` | Телепорт на арену |
| `!arena_del <номер>` | Удалить арену |
| `!arena_save` | Сохранить в `spawns/<карта>.txt`, арены сразу перезагружаются |

Направление взгляда в момент `!arena_ct` / `!arena_t` станет направлением игрока при появлении.

---

## Конфиг (`GP_Duel.json`)
| Параметр | По умолчанию | Описание |
|---|---|---|
| `Database` | пусто | `Host`, `Port`, `User`, `Password`, `Database`. Пустой `Host` = без базы |
| `ChatPrefix` | `{red}[DUEL]{default}` | Префикс в чате |
| `CasualKillsToWin` | `5` | До скольких убийств идёт случайная дуэль |
| `CasualModes` | `normal, normal, awp, pistol` | Режимы случайных дуэлей (`normal`, `awp`, `pistol`, `deagle`, `knife`); повтор режима повышает его шанс |
| `CasualShowScore` | `false` | Показывать счёт и в случайных дуэлях |
| `SoloMaxWaitSeconds` | `20` | Сколько ждёт игрок без пары, прежде чем пары перемешаются (0 = выкл.) |
| `WinOptions` | `3, 5, 10, 15, 0` | Варианты «до скольких побед» (0 = Cheksiz) |
| `ChallengeWeapons` | AK, M4, AWP, Scout, Deagle… | Список «Qurol tanlash» |
| `InviteTimeoutSeconds` | `30` | Сколько действует приглашение |
| `RematchTimeoutSeconds` | `15` | Сколько ждать ответа на реванш |
| `AllowBotChallenge` | `false` | Можно вызывать ботов (для теста) |
| `PrimaryWeapons` / `SecondaryWeapons` | … | Что можно выбрать в «Asosiy qurol» / «To'pponcha» |
| `DefaultPrimary` / `DefaultSecondary` | AK-47 / USP-S | Оружие по умолчанию |
| `GiveArmor` / `GiveHelmet` | `true` | Броня и шлем |
| `RoundDelaySeconds` | `1.0` | Пауза после убийства до следующего раунда пары |
| `SwapSidesEachRound` | `true` | Каждый раунд стороны арены случайно меняются |
| `RandomizeArena` | `true` | Случайная свободная арена |
| `HudEnabled` | `true` | HUD в центре экрана |
| `AdminFlag` | `@css/root` | Флаг редактора арен |
| `ServerCommands` | … | Квары режима, выполняются при каждой смене карты |

---

## Сборка
GitHub Actions собирает плагин при каждом коммите в `main` (`.github/workflows/build.yml`). Вручную: Actions → **Build GP_Duel** → **Run workflow**.

После обновления CounterStrikeSharp на сервере поменяй версию в `GP_Duel.csproj` и сделай коммит.
