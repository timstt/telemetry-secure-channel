#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
channel.py — еталонна модель каналу для Проєкту 1.

ЦЕЙ ФАЙЛ ВИДАЄТЬСЯ ГОТОВИМ І НЕ Є ПРЕДМЕТОМ РЕАЛІЗАЦІЇ.
Модель каналу не належить до тем дисципліни. Вона видається кодом, щоб усі
команди отримували однакову послідовність втрат і однакові значення R та Q.
Змінювати файл не можна: результати перестануть бути порівнянними.

Модель втрат — двостанний ланцюг Маркова (GOOD / BAD):

    GOOD: перед обробкою поточного пакета з імовірністю p_gb система
          переходить у BAD; пакет, на якому стався перехід, є ПЕРШИМ
          втраченим пакетом пачки. Інакше пакет доставляється.
    BAD:  пакет втрачається; довжина пачки — геометрична з середнім B

Середня довжина серії доставлених пакетів дорівнює (1 - p_gb) / p_gb, середня
довжина пачки втрат дорівнює B. Частка втрачених пакетів за цикл:

    loss = B / (B + (1 - p_gb) / p_gb)

Звідси потрібне p_gb:

    p_gb = loss / (loss + B * (1 - loss))

За B = 1 модель вироджується у незалежні втрати Бернуллі.

Час передавання пакета: transmission_time = packet_bits / bandwidth_bps.
Затримка додається до часу прибуття і не впливає на факт втрати.

Бюджет часу: якщо clock + transmission_time перевищує transmission_budget_s,
пакет не передається. Через це пропускна здатність і розмір пакетів прямо
впливають на те, скільки кадрів узагалі дійде, а отже на R і на Q. Саме тому
має сенс і жадібний планувальник, і рюкзак: класти в пакет треба найцінніше,
бо каналу на все не вистачить.
"""

import random


class Channel:
    def __init__(self, scenario, seed):
        """scenario — словник із секції "канал"->"сценарії" файлу team_config.json."""
        self.loss = float(scenario["loss_probability"])
        burst = scenario.get("втрати_пачками") or {}
        self.B = float(burst.get("середня_довжина_пачки", 1))
        d = scenario["delay_ms"]
        self.delay_kind = d["тип"]
        self.delay_const = d.get("значення", 0)
        self.delay_lo = d.get("від", 0)
        self.delay_hi = d.get("до", 0)
        self.bandwidth = float(scenario["bandwidth_bps"])
        # Бюджет часу передавання. Пакет, який не встигає передатися в межах
        # бюджету, не передається зовсім. Саме це робить пропускну здатність
        # реальним обмеженням, а не довідковим числом.
        self.budget_s = float(scenario.get("transmission_budget_s", float("inf")))
        self.rng = random.Random(seed)
        self.p_gb = (self.loss / (self.loss + self.B * (1.0 - self.loss)))             if self.loss > 0 else 0.0
        self.in_burst = 0
        self.clock_s = 0.0
        self.sent = 0
        self.lost = 0
        self.over_budget = 0

    def _delay_ms(self):
        if self.delay_kind == "стала":
            return float(self.delay_const)
        return self.rng.uniform(self.delay_lo, self.delay_hi)

    def send(self, packet_bytes):
        """Повертає (доставлено, час_прибуття_с). Втрачений пакет -> (False, None)."""
        bits = len(packet_bytes) * 8
        need = bits / self.bandwidth
        if self.clock_s + need > self.budget_s:
            # бюджет вичерпано: пакет не передається
            self.over_budget += 1
            return False, None
        self.clock_s += need
        self.sent += 1

        if self.in_burst > 0:
            self.in_burst -= 1
            lost = True
        elif self.loss > 0 and self.rng.random() < self.p_gb:
            # довжина пачки — геометричний розподіл із середнім B
            length = 1
            if self.B > 1.0:
                while self.rng.random() > 1.0 / self.B:
                    length += 1
            self.in_burst = length - 1
            lost = True
        else:
            lost = False

        if lost:
            self.lost += 1
            return False, None
        return True, self.clock_s + self._delay_ms() / 1000.0

    def stats(self):
        total = self.sent + self.over_budget
        return {"надіслано": self.sent, "втрачено": self.lost,
                "не_вміщено_в_бюджет": self.over_budget,
                "частка_втрат": (self.lost / self.sent) if self.sent else 0.0,
                "частка_недоставлених": ((self.lost + self.over_budget) / total)
                                        if total else 0.0,
                "час_каналу_с": self.clock_s,
                "бюджет_с": self.budget_s}
