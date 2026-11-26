# -*- coding: utf-8 -*-
"""
lua_parse.py — 解析 jws2 工程的 Lua 数据表（机器生成格式）
- 基文件 Data/Xxx.lua: propertyTable(字段→列号) / primaryTable(主键) / dataTable(行指针或内联数据)
- 页文件 Data/Xxx-pageN.lua: local dataTable = { {行数组}, ... }
- 行内可含嵌套表 {k=v,...}、{v1,v2}、字符串(带转义)、数字、true/false
输出统一 Python dict/list，可 json 序列化。
"""
import json
import os
import re
import glob


# ---------------------------------------------------------------- 基础扫描

def _skip_ws_comment(text, i):
    """跳过空白与注释（-- 行注释 / --[[ 块注释]]），返回新下标"""
    n = len(text)
    while i < n:
        c = text[i]
        if c in ' \t\r\n':
            i += 1
        elif text.startswith('--[[', i):
            end = text.find(']]', i + 4)
            i = n if end < 0 else end + 2
        elif text.startswith('--', i):
            end = text.find('\n', i)
            i = n if end < 0 else end
        else:
            break
    return i


def _match_brace(text, start):
    """start 指向 '{'，字符串/注释感知地找到配对 '}'，返回其下标"""
    depth = 0
    i = start
    n = len(text)
    while i < n:
        c = text[i]
        if c in '"\'':
            q = c
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == q:
                    break
                i += 1
            i += 1
            continue
        if text.startswith('--[[', i):
            end = text.find(']]', i + 4)
            i = n if end < 0 else end + 2
            continue
        if text.startswith('--', i):
            end = text.find('\n', i)
            i = n if end < 0 else end
            continue
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise ValueError('braces not balanced')


def extract_table_block(text, varname):
    """提取 `local <varname> = { ... }` 或 `<varname> = { ... }` 的完整花括号块文本（含花括号）"""
    m = re.search(r'(?:local\s+)?' + re.escape(varname) + r'\s*=\s*\{', text)
    if not m:
        return None
    start = m.end() - 1
    end = _match_brace(text, start)
    return text[start:end + 1]


# ---------------------------------------------------------------- Lua 表解析

_IDENT_RE = re.compile(r'[A-Za-z_]\w*')
_NUM_RE = re.compile(r'-?\d+\.\d+(?:[eE][+-]?\d+)?|-?\d+(?:[eE][+-]?\d+)?|-?\.\d+')


class _Parser:
    """递归下降解析一个 `{...}` 块（传入含花括号的完整文本）"""

    def __init__(self, text):
        self.t = text
        self.i = 0

    def _ws(self):
        self.i = _skip_ws_comment(self.t, self.i)

    def parse(self):
        self._ws()
        if self.t[self.i] != '{':
            raise ValueError('expect { at %d' % self.i)
        self.i += 1
        arr = []
        obj = {}
        is_array = True
        idx = 1
        while True:
            self._ws()
            if self.i >= len(self.t):
                break
            c = self.t[self.i]
            if c == '}':
                self.i += 1
                break
            if c in ',;':
                self.i += 1
                continue
            key = None
            if c == '[':
                self.i += 1
                self._ws()
                if self.t[self.i] in '"\'':
                    key = self._string()
                else:
                    key = self._number()
                self._ws()
                if self.t[self.i] != ']':
                    raise ValueError('expect ] at %d' % self.i)
                self.i += 1
                self._ws()
                if self.t[self.i] != '=':
                    raise ValueError('expect = at %d' % self.i)
                self.i += 1
                val = self._value()
            elif c in '"\'':
                save = self.i
                s = self._string()
                self._ws()
                if self.i < len(self.t) and self.t[self.i] == '=':
                    self.i += 1
                    key = s
                    val = self._value()
                else:
                    self.i = save
                    val = self._value()
            elif c.isalpha() or c == '_':
                m = _IDENT_RE.match(self.t, self.i)
                ident = m.group(0)
                j = self._ws_after(m.end())
                if j < len(self.t) and self.t[j] == '=' and self.t[j + 1:j + 2] != '=':
                    self.i = j + 1
                    key = ident
                    val = self._value()
                else:
                    self.i = m.end()
                    if ident == 'true':
                        val = True
                    elif ident == 'false':
                        val = False
                    elif ident == 'nil':
                        val = None
                    else:
                        raise ValueError('unexpected ident %r at %d' % (ident, self.i))
            else:
                val = self._value()

            if key is None:
                arr.append(val)
                obj[idx] = val
                idx += 1
            else:
                is_array = False
                obj[key] = val
        if is_array and arr:
            return arr
        # Lua semantics: a table whose only keys are consecutive integers 1..n is an array even
        # though each element carries named fields (arrays of named rows are common in these
        # sheets: SkillEvent_Table, SkillSwitch_Table, WeightMonsterIDList, …); an empty table
        # in a data column is an empty list.
        if not obj:
            return []
        if not any(not isinstance(k, int) for k in obj) and sorted(obj) == list(range(1, len(obj) + 1)):
            return [obj[i] for i in range(1, len(obj) + 1)]
        return obj

    def _ws_after(self, i):
        return _skip_ws_comment(self.t, i)

    def _string(self):
        q = self.t[self.i]
        i = self.i + 1
        out = []
        t = self.t
        while i < len(t):
            c = t[i]
            if c == '\\':
                nxt = t[i + 1]
                mapping = {'n': '\n', 't': '\t', 'r': '\r', '"': '"', "'": "'", '\\': '\\'}
                out.append(mapping.get(nxt, nxt))
                i += 2
                continue
            if c == q:
                self.i = i + 1
                return ''.join(out)
            out.append(c)
            i += 1
        raise ValueError('unterminated string')

    def _number(self):
        m = _NUM_RE.match(self.t, self.i)
        if not m:
            raise ValueError('bad number at %d' % self.i)
        self.i = m.end()
        s = m.group(0)
        try:
            return int(s)
        except ValueError:
            return float(s)

    def _value(self):
        self._ws()
        c = self.t[self.i]
        if c == '{':
            return self.parse()
        if c in '"\'':
            return self._string()
        if c == '-' or c.isdigit():
            return self._number()
        m = _IDENT_RE.match(self.t, self.i)
        if m:
            ident = m.group(0)
            self.i = m.end()
            if ident == 'true':
                return True
            if ident == 'false':
                return False
            if ident == 'nil':
                return None
        raise ValueError('bad value at %d: %r' % (self.i, self.t[self.i:self.i + 20]))


def parse_lua_table(block_text):
    """解析含花括号的完整块文本为 Python list/dict"""
    return _Parser(block_text).parse()


# ---------------------------------------------------------------- 数据表读取

def parse_setmetatable_rows(block_text):
    """解析 dataTable 块：兼容 `{...}, {...}` 与 `setmetatable({...},base), ...` 两种行格式。
    传入的块含最外层花括号，此处先剥掉再逐条解析。"""
    t = block_text.strip()
    if t.startswith('{') and t.endswith('}'):
        t = t[1:-1]
    rows = []
    i = 0
    n = len(t)
    while i < n:
        i = _skip_ws_comment(t, i)
        if i >= n:
            break
        c = t[i]
        if c in ',;':
            i += 1
            continue
        if c == '}':
            break
        if t.startswith('setmetatable', i):
            j = t.index('(', i)
            k = _skip_ws_comment(t, j + 1)
            if t[k] != '{':
                raise ValueError('expect { after setmetatable( at %d' % k)
            e = _match_brace(t, k)
            rows.append(parse_lua_table(t[k:e + 1]))
            i = e + 1
            # 跳过 ', base)' 尾部
            close = t.index(')', i)
            i = close + 1
        elif c == '{':
            e = _match_brace(t, i)
            rows.append(parse_lua_table(t[i:e + 1]))
            i = e + 1
        else:
            raise ValueError('unexpected char %r at %d' % (c, i))
    return rows


def _page_no(path):
    m = re.search(r'-page(\d+)\.lua$', path)
    return int(m.group(1)) if m else 0


class DataSheet:
    """一张数据表：rows 为按 propertyTable 映射后的 dict 列表，keyed 为主键元组→行"""

    def __init__(self, name, property_table, primary, rows):
        self.name = name
        self.property_table = property_table
        self.primary = primary
        self.rows = rows
        self.keyed = {}
        for row in rows:
            try:
                k = tuple(row.get(pk) for pk in primary)
                self.keyed[k] = row
            except Exception:
                pass

    def get(self, *keys):
        return self.keyed.get(tuple(keys))

    def find(self, **field_eq):
        out = []
        for row in self.rows:
            if all(row.get(k) == v for k, v in field_eq.items()):
                out.append(row)
        return out


def load_sheet(data_dir, name, cache_dir=None):
    """读取 Data/<name>.lua（含 -pageN），返回 DataSheet。cache_dir 提供时缓存 JSON。"""
    cache = None
    if cache_dir:
        cache = os.path.join(cache_dir, 'sheet_' + name + '.json')
        if os.path.isfile(cache):
            with open(cache, 'r', encoding='utf-8') as f:
                d = json.load(f)
            return DataSheet(d['name'], d['property_table'], d['primary'], d['rows'])

    base_path = os.path.join(data_dir, name + '.lua')
    with open(base_path, 'r', encoding='utf-8', errors='replace') as f:
        base_text = f.read()

    prop = parse_lua_table(extract_table_block(base_text, 'propertyTable'))
    pt_block = extract_table_block(base_text, 'primaryTable')
    primary = parse_lua_table(pt_block) if pt_block else ['ID']

    rows = []
    pages = sorted(glob.glob(os.path.join(data_dir, name + '-page*.lua')), key=_page_no)
    if pages:
        for p in pages:
            with open(p, 'r', encoding='utf-8', errors='replace') as f:
                block = extract_table_block(f.read(), 'dataTable')
            for raw in parse_setmetatable_rows(block):
                rows.append(_map_row(prop, raw))
    else:
        block = extract_table_block(base_text, 'dataTable')
        if block:
            for raw in parse_setmetatable_rows(block):
                rows.append(_map_row(prop, raw))

    sheet = DataSheet(name, prop, primary if isinstance(primary, list) else [primary], rows)
    if cache_dir:
        os.makedirs(cache_dir, exist_ok=True)
        with open(cache, 'w', encoding='utf-8') as f:
            json.dump({'name': name, 'property_table': prop,
                       'primary': sheet.primary, 'rows': rows}, f, ensure_ascii=False)
    return sheet


def _map_row(prop, raw):
    row = {}
    for field, idx in prop.items():
        v = raw[idx - 1] if isinstance(idx, int) and 0 < idx <= len(raw) else None
        row[field] = v
    # 保留原始位置值兜底（超出 propertyTable 的列）
    row['_raw'] = raw
    return row


# ---------------------------------------------------------------- 其他格式

def load_cn_txt(path):
    """Language/CN.txt: Key=Value 行 → dict"""
    cn = {}
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            if '=' in line:
                k, _, v = line.partition('=')
                cn[k.strip()] = v
    return cn


def load_json(path):
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)
