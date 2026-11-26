# -*- coding: utf-8 -*-
"""Puts `Tools/` on sys.path so the tests import the converters as `sanguo_import.<module>`."""

import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))
