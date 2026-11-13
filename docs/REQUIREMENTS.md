# Крок 1. Вимоги, пастки, інтерфейси

Зведення з `docs/PROJECT_1.md`, `docs/FORMAT_SPEC.md`, `docs/CHANNEL_SPEC.md`, `channel/channel.py`,
`data/team_config.json` і `data/reference/*`. Де документ і промпт розходяться — посилання на першоджерело.
Числа з прототипу (пастки 18–21) — орієнтир для самоперевірки, а не офіційні результати.

## (a) Вимоги

### C1 — 12 базових приймальних кейсів (рівень = пройдені / 12)

| № | Вимога | Критерій тесту | Власник | Тест |
|---:|---|---|---|---|
| 1 | Потік з одного кадру проходить і відновлюється побайтово | 1 кадр → спокійний канал → приймач; 12 Б == вхідні; ref_01 відтворюється байт у байт (107 Б) | тімлід + усі | `Acceptance/C1_Case01Tests` |
| 2 | Порожній потік не валить систему | 0 кадрів: без винятків; ref_02 (89 Б, frame_count = 0) читається | тімлід, Коля | `C1_Case02Tests` |
| 3 | Спокійний сценарій: усі кадри відновлено точно | session.csv (3000 кадрів), 5 seed: множина відновлених == вхідна | тімлід | `C1_Case03Tests` |
| 4 | Робочий: втрата критичних ≤ порогу (0.05) | 1 − R ≤ 0.05 на кожному з 5 seed | тімлід | `C1_Case04Tests` |
| 5 | Аварійний: без падіння, Q обчислено | 5 seed, лог без необроблених винятків, Q ∈ [0,1]. Поріг 0.2 у C1 НЕ входить | тімлід | `C1_Case05Tests` |
| 6 | Дерево Гаффмана відновлюється з канонічних довжин | коди за §3.3 == «код» у ref_*.json; декодування == кадри json | Саша | `C1_Case06Tests` |
| 7 | Вироджений алфавіт | 1 символ → length = 1, код «0»; ref_03 читається | Саша | `C1_Case07Tests` |
| 8 | ДП ≥ жадібного на всіх наборах | Σv(ДП) ≥ Σv(жадібний), Σw(ДП) ≤ W | Георгій, Саша | `C1_Case08Tests` |
| 9 | Клас і пріоритет усім кадрам; macro-F1 ≥ 0.58 на holdout | довжина виходу == кількість кадрів; класи 0..2; пріоритет 1/3/5 | Ваня | `C1_Case09Tests` |
| 10 | Змінений байт і змінений HMAC відхиляються | bad_01 → E_HMAC, bad_02 → E_HMAC | Коля | `C1_Case10Tests` |
| 11 | Невірний пароль → відмова, а не сміття; обрізаний → відмова без падіння | E_HMAC, payload == null; bad_03 → E_LENGTH | Коля | `C1_Case11Tests` |
| 12 | Еталонні пакети приймаються; повний прогін ≤ 120 с | усі ref_* Ok, bad_* за expected_failures.json; Runner 3×5 ≤ 120 с | Коля, тімлід | `C1_Case12Tests` |

### C2 — крайні випадки (5 пунктів по 1 балу)

| п. | Вимога | Як перевіряємо | Власник |
|---:|---|---|---|
| 1 | Валідація вхідної телеметрії: діапазони, типи, пропуски | `Frame.ParseCsv`: mode ∉ 0..7, critical ∉ {0,1}, поза int16/uint32, порожнє поле → виняток; Runner пропускає й логує рядок | Ваня |
| 2 | Вироджений алфавіт: один символ і всі 256 рівноймовірні | length = 1 / усі довжини = 8, encode→decode тотожність | Саша |
| 3 | Немає необроблених винятків у лог-файлі повного прогону | Runner логує всі винятки; тест: після прогону лог не містить `Unhandled` | тімлід |
| 4 | Межові значення: пакет розміру 0, кадр більший за пакет, потік довший за пам'ять | 0 Б → E_SHORT; W ≤ 0 / w_i > W → кадр не обирається, без зациклення; потоковий (не весь файл у пам'ять) читач session.bin | тімлід, Георгій |
| 5 | Свідома відмова від стиснення при високій ентропії | NO_COMPRESSION (flags 0x08), ref_05 читається; поріг — ген ГА | Саша |

### C5 — цілісність і безпека (5 пунктів по 0,6)

| п. | Вимога | Як перевіряємо | Власник |
|---:|---|---|---|
| 1 | Валідація заголовка до розшифрування (MAGIC, версія, довжини) | bad_03 → E_LENGTH, bad_04 → E_MAGIC без виклику CTR | Коля |
| 2 | HMAC до будь-якої обробки вмісту | bad_05 (reserved = 1 під валідним HMAC) → E_PAYLOAD, а bad_01 → E_HMAC (а не E_PAYLOAD) | Коля |
| 3 | Ключі й пароль не в логах і не у відкритій частині пакета; сіль відкрита | тест: лог прогону і всі пакети не містять пароля, K_enc, K_mac | Коля, тімлід |
| 4 | Атомарний запис відновлених даних | запис у `*.tmp` + `File.Move(overwrite)`; тест: збій посередині не псує попередній файл | тімлід |
| 5 | Стійкість до обрізаного або переставленого потоку | усі префікси пакета → відмова без винятку; переставлені й дубльовані пакети → кадри відсортовані за timestamp_ms | Коля |

## (b) Пастки і тести

| # | Пастка | Тест (каркас, поки `Skip`) |
|---:|---|---|
| 1 | Big-endian, біти MSB-first, нульове доповнення | `Huffman/HuffmanTrapTests.Trap01_*`, `Predict/PredictTrapTests.Frame_roundtrip_big_endian_ref01` |
| 2 | Канонічні коди за §3.3, ref_01: довжини 1,3,3,4,4,4,4, потік `0C E6 DF 90` | `Trap02_ref01_lengths_codes_and_bitstream` |
| 3 | Ліміт 15 біт без злиття символів; вироджений алфавіт length = 1 | `Trap03_code_length_limited_to_15_*`, `Trap03_degenerate_*` |
| 4 | symbol_count = 12·frame_count, інакше E_PAYLOAD; NO_COMPRESSION | `Trap04_no_compression_*`, `ReceiverTrapTests.Reference_packets_*` (ref_05) |
| 5 | Приймач не відхиляє пакет за розміром (ref_04 = 771 Б) | `Trap05_oversized_ref04_is_accepted` |
| 6 | Порядок приймача, FixedTimeEquals, невірний пароль = E_HMAC | `Bad_packets_give_expected_code`, `Trap06_*` |
| 7 | KDF 100000/SHA256/64 Б, різні K_enc/K_mac, кеш на сеанс | `Trap07_kdf_keys_are_different_and_cached` |
| 8 | Власний CTR, лічильник з 1, nonce 8 Б сеансу ‖ seq | `Trap08_ctr_counter_starts_at_1_ref01_ciphertext` |
| 9 | HMAC над заголовком + шифротекстом | `Trap09_hmac_covers_header_and_ciphertext` |
| 10 | Вікно 64, одна таблиця, w_i, W, CLASS_TABLE = 0 | `ConfigTests.Knapsack_capacity_formula` (зелений), `PackingTrapTests.*` |
| 11 | Канал лише через незмінений channel.py | `DataLayoutTests.Channel_py_is_unchanged` (зелений) + CI sha256, `Trap11_bridge_*` |
| 12 | Відкритий seed лише для налагодження, 5 seed | Runner/MeasureQ приймають список seed; тест: Q рахується як середнє за 5 |
| 13 | Тільки власний код для Гаффмана, ДП, ГА, перцептрона | рев'ю PR + перевірка `PackageReference` у CI-рев'ю |
| 14 | Метрика: одиниці в бітах, B_sent з втраченими, без невміщених, Q = 0 при R = 0 / падінні | `Trap14_metric_units_and_zero_rules`, `ConfigTests.Metric_coefficients_*` (зелений) |
| 15 | flags & 0x03 ≠ 0 без підтримки → E_PAYLOAD | `Trap15_unknown_extension_flag_is_E_PAYLOAD` |
| 16 | Кадри за timestamp_ms; seq після планувальника, без дірок; дублікати seq ігноруються | `Trap16_duplicates_and_reordering_*` |
| 17 | Дисбаланс класів train.csv 6243/801/956 | `DataLayoutTests.Train_has_known_class_imbalance` (зелений) |
| 18 | Черга за цінністю, вікно = перші 64 черги | тест Sender: у перших пакетах частка критичних ≥ частки в сеансі |
| 19 | Ціна таблиці: вибір режиму Гаффман/NO_COMPRESSION на пакет | тест Sender: обирається режим з більшою цінністю на біт |
| 20 | Бюджет: в аварійному проходить ≈50 із ≈190–210 пакетів | спостереження → README «Питання до викладача» |
| 21 | Пороги R: №4 ≥ 0.95 на кількох seed; №5 — лише без падіння | `C1_Case04Tests`, `C1_Case05Tests` |

## (c) Інтерфейси між модулями

Усі контракти — `src/Telemetry.Core/Interfaces.cs`. Залежності проєктів:

```
Core  ←  Huffman, Predict, Security, Packing, Optimize
Core, Huffman, Security  ←  Packet
Core, Huffman, Predict, Security, Packet, Packing  ←  System
System, Optimize  ←  Runner;   System, Packet  ←  Tools
```

| Контракт | Постачальник | Споживач |
|---|---|---|
| `Frame` (12 Б BE, валідація) | Ваня | усі |
| `IHuffmanCodec` | Саша (`HuffmanCodec`) | Packet, Sender |
| `IPacker` | Георгій (`KnapsackDp`), Саша (`KnapsackGreedy`) | Sender, таблиця 1 |
| `IClassifier` | Ваня (`Classifier`) | Sender |
| `ICrypto` | Коля | Sender, Receiver, таблиця 3 |
| `IScheduler` | тімлід (`Scheduler`) | Sender |
| `IOptimizer` | Георгій (`GeneticAlgorithm`, `RandomSearch`) | Runner |
| `IChannel` | тімлід (`ChannelBridge` + `channel/bridge.py`) | Sender, Runner |
| `PacketPlan`, `OpenResult`, `PacketError`, `Params`, `EvolutionLog`, `ChannelStats` | тімлід | усі |

Конвеєр Sender: класифікація → черга за v_i ↓ (рівні — timestamp_ms) → перші 64 кадри → одна таблиця
Гаффмана → w_i → ДП (W) → вибір режиму Гаффман/NO_COMPRESSION → `PacketPlan` → `IScheduler.Order` →
seq у порядку відправлення → `ICrypto.Seal` → `IChannel.Send`.
