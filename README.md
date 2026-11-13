# Захищений канал телеметрії з обмеженою пропускною здатністю

Командний проєкт 1 з дисципліни «Алгоритми і оптимізація програмних рішень», команда **T01**.
Передавач прогнозує клас кадру (перцептрон), стискає (канонічний Гаффман), пакує (рюкзак 0/1 — ДП),
планує чергу (жадібний алгоритм), шифрує (AES-256-CTR + HMAC-SHA256); канал — готовий `channel.py`;
приймач перевіряє HMAC, розшифровує і відновлює кадри. Параметри налаштовує генетичний алгоритм.

> Статус: **каркас**. Контракти й заглушки на місці, реалізації зон — у наступних PR.

## Запуск з нуля

Потрібно: .NET 8 SDK, Python 3.10+.

```bash
dotnet test                                    # збірка + усі тести
dotnet run --project src/Telemetry.Runner      # повний прогін: 3 сценарії × 5 seed (після інтеграції)
```

Перевірки, які запускає CI:

```bash
dotnet build -c Release                        # Roslyn-аналізатори, попередження = помилки
dotnet format --verify-no-changes
dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=total
```

## Структура

| Тека | Зміст | Власник |
|---|---|---|
| `src/Telemetry.Core` | `Interfaces.cs`, `Config.cs` (team_config.json), `Frame.cs` | тімлід; `Frame` — Ваня |
| `src/Telemetry.Huffman` | BitWriter/BitReader, канонічний Гаффман, NO_COMPRESSION, жадібний рюкзак | Саша |
| `src/Telemetry.Predict` | Mlp, Features, Classifier | Ваня |
| `src/Telemetry.Security` | Kdf, CtrCipher, Mac | Коля |
| `src/Telemetry.Packet` | Packet, Errors, Receiver | Коля |
| `src/Telemetry.Packing` | KnapsackDp | Георгій |
| `src/Telemetry.Optimize` | GeneticAlgorithm, RandomSearch | Георгій |
| `src/Telemetry.System` | Scheduler, Sender, Metric, ChannelBridge | тімлід |
| `src/Telemetry.Runner` | консольний прогін сценаріїв | тімлід |
| `tools/Telemetry.Tools` | `check-ref`, `measure-q` (тімлід), `report` (Саша) | |
| `tests/Telemetry.Tests` | xUnit: тека на зону + `Acceptance/C1_Case01..12` | усі |
| `channel/` | `channel.py` (**змінювати заборонено**, CI перевіряє sha256), `bridge.py` | — / тімлід |
| `data/` | матеріали викладача: team_config, csv, session.bin, `reference/` | — |
| `docs/` | `PROJECT_1.md`, `FORMAT_SPEC.md`, `CHANNEL_SPEC.md`, `REQUIREMENTS.md`, `DEFENSE_NOTES/` | |

Вимоги C1/C2/C5, пастки і контракти зведено в [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md).

## Правила роботи

- Кожна зміна: issue → гілка `feat/<зона>/<опис>` → PR → рев'ю іншого учасника → merge. Напряму в `main` не пушимо.
- Рев'ю по колу: Саша → Георгій → Ваня → Коля → тімлід → Саша. Рецензент записує щонайменше одне питання до коду.
- Зміна контракту в `Interfaces.cs` — тільки через issue тімліду.
- Тести зони в каркасі позначені `Skip = TestData.Pending`. Реалізуючи клас, власник прибирає `Skip` у своїх тестах
  і атрибут `[ExcludeFromCodeCoverage]` з класу (він стоїть лише на заглушках, щоб поріг покриття 80 % рахувався
  по реальному коду).
- Власний код для Гаффмана, ДП, перцептрона, ГА. Бібліотеки — лише `System.Security.Cryptography`, файли, графіки, xUnit.
- Усе, що згенеровано ШІ, фіксується в [`META-USAGE.md`](META-USAGE.md).
- Простір імен `Telemetry.System` перекриває `System` всередині `Telemetry.*`: повні імена пишемо як
  `global::System.IO.Path` (або через `using`).

## Метрика Q і припущення про одиниці

`Q = 0.5·S + 0.35·R + 0.15·T`, `S = B_ref/(B_ref + B_sent)`, `T = 20/(20 + t)`.

- `B_ref = 8 · 12 · кількість_кадрів` біт; `B_sent` — теж у бітах: усі біти, віддані в канал, включно з утраченими
  пакетами; пакети, які канал не передав через бюджет, у `B_sent` не входять.
- `t` — час обчислень системи; час каналу (`stats()["час_каналу_с"]`) у `t` не входить і наводиться окремо.
- Приймач упав або `R = 0` → `Q = 0`.

## Питання до викладача

1. Чи правильно ми трактуємо одиниці `B_ref`/`B_sent` (біти, втрачені пакети враховуються, невміщені в бюджет — ні)?
2. Прототип дає ≈186–210 пакетів на сеанс, з яких в аварійному сценарії (16.9 с, 12 кбіт/с) передається ≈50,
   а в завданні сказано «близько третини не проходить». Чи оцінка робилась для іншого розміру пакетів?
   Канал ми не змінюємо.
