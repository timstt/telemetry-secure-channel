# Захищений канал телеметрії з обмеженою пропускною здатністю

[![CI](https://github.com/timstt/telemetry-secure-channel/actions/workflows/ci.yml/badge.svg)](https://github.com/timstt/telemetry-secure-channel/actions/workflows/ci.yml)

Командний проєкт 1 з дисципліни «Алгоритми і оптимізація програмних рішень», команда **T01**, C# / .NET 8.

Апарат передає телеметрію на наземну станцію вузьким каналом із втратами, який прослуховують. Система:

```
телеметрія → [1] перцептрон → [2] Гаффман → [3] рюкзак (ДП) → [4] жадібна черга → [5] AES-CTR + HMAC → КАНАЛ → приймач
                (клас кадру)    (стиснення)    (що в пакет)      (порядок)          (захист)          channel.py
                                     ↑ [6] генетичний алгоритм налаштовує параметри блоків
```

Мета — максимізувати метрику `Q = 0.5·S + 0.35·R + 0.15·T`: менше біт, більше доставлених критичних кадрів, швидка робота.

> **Статус:** каркас змерджено. Контракти, заглушки, тести-пастки й CI на місці; реалізації зон — у PR учасників.
> Тести зон поки позначені `Skip` і вмикаються власниками.

---

## Запуск з нуля

Потрібно: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) і Python 3.10+ (`python3` у `PATH`, лише стандартна бібліотека).

```bash
git clone https://github.com/timstt/telemetry-secure-channel && cd telemetry-secure-channel
dotnet test
```

Повний прогін (після інтеграції модулів): 3 сценарії × 5 seed, лог і результати в `out/`.

```bash
dotnet run --project src/Telemetry.Runner
```

Ті самі перевірки, що в CI (запускати перед кожним PR):

```bash
dotnet build -c Release                        # Roslyn-аналізатори, попередження = помилки
dotnet format --verify-no-changes              # стиль; виправити: dotnet format
dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=total
```

Інструменти:

```bash
dotnet run --project tools/Telemetry.Tools -- check-ref   # усі ref_* приймаються, bad_* дають очікуваний код
dotnet run --project tools/Telemetry.Tools -- measure-q   # Q за сценаріями і seed
dotnet run --project tools/Telemetry.Tools -- report      # 5 таблиць звіту з реальних прогонів
```

---

## Команда і зони

| Хто | Тема захисту | Модулі | Закриває |
|---|---|---|---|
| **Саша** | Жадібні алгоритми | `Telemetry.Huffman`: BitWriter/BitReader, канонічний Гаффман (ліміт 15 біт), NO_COMPRESSION, KnapsackGreedy; `report` | C1 №6, №7; C2 п.2, п.5; таблиця 3; жадібна частина таблиці 1 |
| **Ваня** | Перцептрон | `Telemetry.Predict`: Mlp, Features, Classifier; `Frame` + валідація входу | C1 №9; C2 п.1 |
| **Коля** | Захист і формат пакета | `Telemetry.Security`: Kdf, CtrCipher, Mac; `Telemetry.Packet`: Packet, Errors, Receiver | C1 №10, №11, №12; C5 |
| **Георгій** | ДП + генетичний алгоритм | `Telemetry.Packing`: KnapsackDp; `Telemetry.Optimize`: GeneticAlgorithm, RandomSearch | C1 №8; ДП-частина таблиці 1; таблиця 5 |
| **Тимофій** (тімлід) | Система в цілому + жадібна черга | `Telemetry.System`: Scheduler, Sender, Metric, ChannelBridge; Runner, `check-ref`, `measure-q`, CI, README | C1 №1–5; C2 п.3, п.4; C3; C4; таблиці 2 і 4 |

Кожен захищає свою частину (E1 — 6 питань, E2 — 2 мікрозміни). Таблицю 1 «ДП проти жадібності» Саша й Георгій
захищають разом.

**Рев'ю по колу (B3):** Саша → Георгій → Ваня → Коля → Тимофій → Саша. Рецензент записує в PR щонайменше одне
питання до коду і отримує відповідь автора.

---

## Як почати роботу над своєю зоною

1. Клонувати репозиторій (див. «Запуск з нуля») і переконатися, що `dotnet test` зелений.
2. Узяти свій issue в трекері (мітка `zone:<твоя зона>`).
3. Створити гілку `feat/<зона>/<опис>`, наприклад `feat/greedy/bitwriter`, `feat/secure/kdf`, `feat/predict/frame`.
4. Реалізуючи клас:
   - прибрати `Skip = TestData.Pending` з тестів своєї зони в `tests/Telemetry.Tests/<зона>/` і відповідних `Acceptance/C1_CaseNN`;
   - прибрати `[ExcludeFromCodeCoverage]` з реалізованого класу (атрибут стоїть лише на заглушках, щоб поріг 80 %
     рахувався по реальному коду);
   - додати власні тести з крайніми випадками.
5. Вести `docs/DEFENSE_NOTES/<зона>.md`: доведення, відповіді на банк питань, мікрозміни, «ворота захисту».
6. PR за шаблоном → рев'ю іншого учасника → зелений CI → merge. Напряму в `main` не пушимо.

Порядок мерджів: `Frame` (Ваня, перший малий PR) → паралельно BitIO/Huffman, Packet/Security/Receiver, Mlp, KnapsackDp,
Bridge/Sender/Metric/Scheduler → тонкий зріз без стиснення → підключення Гаффмана, ДП, класифікатора → прогін
3 сценаріїв і Acceptance C1 №1–12 → ГА → звіт → запуск з нуля всією командою.

**Контракти** між модулями — `src/Telemetry.Core/Interfaces.cs`. Змінювати сигнатури лише через issue тімліду.
Повний список вимог C1/C2/C5, 21 пастка з тестами і схема інтерфейсів — у [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md).

---

## Структура репозиторію

| Тека | Зміст |
|---|---|
| `src/Telemetry.Core` | `Interfaces.cs` (контракти), `Config.cs` (типізоване читання `team_config.json`), `Frame.cs` |
| `src/Telemetry.Huffman` | біти, канонічний Гаффман, NO_COMPRESSION, жадібний рюкзак |
| `src/Telemetry.Predict` | перцептрон, ознаки з вікном історії, класифікатор |
| `src/Telemetry.Security` | PBKDF2, власний AES-256-CTR, HMAC-SHA256 |
| `src/Telemetry.Packet` | формат пакета, коди відмови, приймач |
| `src/Telemetry.Packing` | рюкзак 0/1 динамічним програмуванням |
| `src/Telemetry.Optimize` | генетичний алгоритм, випадковий пошук |
| `src/Telemetry.System` | планувальник, передавач, метрика, міст до каналу |
| `src/Telemetry.Runner` | консольний прогін сценаріїв |
| `tools/Telemetry.Tools` | `check-ref`, `measure-q`, `report` |
| `tests/Telemetry.Tests` | xUnit: тека на зону, `Integration/`, `Acceptance/C1_Case01..12` |
| `channel/` | `channel.py` від викладача, `bridge.py` (міст stdin/stdout для C#) |
| `data/` | матеріали викладача |
| `docs/` | завдання, специфікації, вимоги, нотатки до захисту |

---

## Матеріали викладача

| Файл | Призначення |
|---|---|
| `docs/PROJECT_1.md` | текст завдання (сконвертовано з docx без змін змісту) |
| `docs/FORMAT_SPEC.md` | формат пакета з точністю до біта |
| `docs/CHANNEL_SPEC.md` | опис моделі каналу |
| `channel/channel.py` | модель каналу. **Змінювати заборонено**: тест і CI звіряють sha256 |
| `data/team_config.json` | коефіцієнти Q, ліміти пакування, пріоритети, сценарії, seed, F1_min |
| `data/train.csv`, `data/valid.csv` | дані перцептрона (8000 і 2000 рядків, мітки 0/1/2) |
| `data/session.csv`, `data/session.bin` | сеанс для вимірювання Q (3000 кадрів, 300 критичних) |
| `data/reference/ref_01..06.bin + .json` | еталонні пакети з розшифрованим вмістом |
| `data/reference/bad_01..05.bin`, `expected_failures.json` | пошкоджені пакети і очікувані коди відмови |

Відкритий seed каналу (`425243`) — лише для налагодження. Офіційне Q викладач рахує на п'яти прихованих seed, тому
ми міряємо на 5 seed і нічого не підганяємо під один.

---

## Метрика Q і наші припущення

```
Q = α·S + β·R + γ·T,   α = 0.5, β = 0.35, γ = 0.15
S = B_ref / (B_ref + B_sent)     R = частка відновлених критичних кадрів     T = t_ref / (t_ref + t),  t_ref = 20 с
```

- `B_ref = 8 · 12 · кількість_кадрів` — у **бітах**; `B_sent` — теж у бітах.
- `B_sent` — усі біти, віддані в канал, **включно з утраченими** пакетами (смуга витрачена).
  Пакети, які канал не передав через вичерпання бюджету, у `B_sent` **не входять**.
- `t` — час обчислень системи на повний прогін. Час каналу (`stats()["час_каналу_с"]`) у `t` не входить і
  наводиться у звіті окремо.
- Приймач аварійно завершився або `R = 0` → `Q = 0`.

## Спостереження, які чесно покажемо у звіті

- **Ціна таблиці Гаффмана.** Таблиця на 64 кадрах ≈129 символів ≈258 Б із 437 Б навантаження, тож у пакет
  влазить ≈14–16 кадрів; без стиснення — 35. Тому Sender для кожного пакета обирає режим (Гаффман або
  NO_COMPRESSION), а поріг — ген ГА. Якщо Гаффман на цих даних знижує Q, так і пишемо в таблиці 2.
- **Черга за цінністю.** Вікно кандидатів — перші 64 кадри черги, впорядкованої за цінністю `v_i`, а не за часом:
  інакше критичні кадри розмазуються по пакетах і в аварійному сценарії R падає.
- Числа вище — з прототипу на Python, орієнтир для самоперевірки, а не офіційні результати.

---

## Правила

- **Власний код** для Гаффмана, ДП, перцептрона і ГА. Без ML.NET, TorchSharp, Accord, оптимізаторів MathNet.
  Дозволено: `System.Security.Cryptography` (AES, SHA, PBKDF2), файли, графіки (ScottPlot), xUnit,
  `PriorityQueue<,>`. `AesGcm` не використовуємо: формат вимагає CTR + окремий HMAC.
- **Чесність результатів:** таблиці звіту генеруються лише з реальних прогонів; блок без виграшу описуємо з поясненням.
- **Кожна зміна:** issue → гілка `feat/<зона>/<опис>` → PR → рев'ю → merge.
- Простір імен `Telemetry.System` перекриває `System` всередині `Telemetry.*`: повні імена пишемо як
  `global::System.IO.Path` або через `using`.

## Типові проблеми

| Симптом | Причина / рішення |
|---|---|
| `CS0234: 'IO' does not exist in the namespace 'Telemetry.System'` | писати `global::System.IO…` або `using System.IO;` |
| CI падає на `dotnet format` | запустити `dotnet format` локально і закомітити |
| CI падає на покритті | додати тести або (лише для ще не реалізованих заглушок) лишити `[ExcludeFromCodeCoverage]` |
| CI падає на перевірці `channel.py` | файл змінено (зокрема переведенням рядків) — відновити: `git checkout main -- channel/channel.py` |
| `ChannelBridge`: не запускається Python | задати інтерпретатор: `TELEMETRY_PYTHON=py` (Windows) або повний шлях; за замовчуванням `python` на Windows, `python3` деінде |
| Тести не бачать `data/` | тести шукають корінь за `Telemetry.sln`; запускати з будь-якої теки всередині репозиторію |
