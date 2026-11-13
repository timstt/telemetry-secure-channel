#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
bridge.py — міст між C# (ChannelBridge.cs) і НЕЗМІНЕНИМ channel.py.

Протокол: один процес на прогін, по рядку JSON у stdin/stdout (UTF-8):
  -> {"init": {"scenario": {...сценарій із team_config...}, "seed": N}}   <- {"ok": true}
  -> {"send": "<hex пакета>"}   <- {"delivered": true|false, "arrival": число|null}
  -> {"stats": 1}               <- словник Channel.stats()
Помилка запиту -> {"error": "<опис>"} (процес не завершується).
Кінець stdin -> вихід з кодом 0.

channel.py змінювати заборонено: послідовність втрат залежить від random.Random Python,
тому канал не переписується на C#. Власник: тімлід.
"""

import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from channel import Channel  # noqa: E402


def handle(request, state):
    if "init" in request:
        init = request["init"]
        state["channel"] = Channel(init["scenario"], init["seed"])
        return {"ok": True}
    channel = state.get("channel")
    if channel is None:
        return {"error": "канал не ініціалізовано: спершу init"}
    if "send" in request:
        delivered, arrival = channel.send(bytes.fromhex(request["send"]))
        return {"delivered": bool(delivered), "arrival": arrival}
    if "stats" in request:
        return channel.stats()
    return {"error": "невідомий запит"}


def main():
    stdin = io.TextIOWrapper(sys.stdin.buffer, encoding="utf-8")
    stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")
    state = {}
    for line in stdin:
        line = line.strip()
        if not line:
            continue
        try:
            response = handle(json.loads(line), state)
        except Exception as exc:  # noqa: BLE001 — відповідь, а не падіння процесу
            response = {"error": type(exc).__name__}
        stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        stdout.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
