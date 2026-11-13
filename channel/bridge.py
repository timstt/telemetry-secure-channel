#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
bridge.py — міст між C# (ChannelBridge.cs) і НЕЗМІНЕНИМ channel.py.

Протокол: один процес на прогін, по рядку JSON у stdin/stdout (UTF-8):
  -> {"init": {"scenario": {...сценарій із team_config...}, "seed": N}}   <- {"ok": true}
  -> {"send": "<hex пакета>"}   <- {"delivered": true|false, "arrival": число|null}
  -> {"stats": 1}               <- словник Channel.stats()

channel.py змінювати заборонено: послідовність втрат залежить від random.Random Python.
Власник: тімлід. Поки що — заглушка каркаса.
"""

import sys

from channel import Channel  # noqa: F401  (буде використано в реалізації)


def main() -> int:
    raise NotImplementedError("bridge.py: ще не реалізовано (каркас)")


if __name__ == "__main__":
    sys.exit(main())
