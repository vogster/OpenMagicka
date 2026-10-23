"""Sync docs/*.md into the ChillaDesk wiki of project OM.

Usage: python tools/wiki_sync.py [--dry-run]
The token is taken from $CHILLADESK_TOKEN or from .mcp.json.
docs/README.md becomes the wiki home page; every other page is created under it.
"""
import json
import os
import re
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = "OM"
PARENT = "OpenMagicka"
PAGES = {
    "docs/README.md": PARENT,
    "docs/ENGINE_ANALYSIS.md": "Engine analysis",
    "docs/TASKS.md": "Backlog",
    "docs/formats/XNB_READERS.md": "XNB formats",
    "docs/formats/LEVELS_TRIGGERS.md": "Levels and triggers",
    "docs/formats/ENUMS.md": "Enumerations",
    "docs/gameplay/SPELLS.md": "Spells and combat",
    "docs/engine/ARCHITECTURE.md": "Engine architecture",
    "docs/engine/ENTITIES_AI.md": "Entities, AI and network",
    "docs/engine/RISK_CHECKS.md": "Stack risk checks",
    "docs/adr/0001-stack.md": "ADR-0001 Stack",
    "docs/adr/0002-network.md": "ADR-0002 Network play",
}
MD_LINK = re.compile(r"\[([^\]]+)\]\(([^)\s]+\.md)\)")


def to_wiki_links(rel, content):
    """Turn relative links to synced docs into [[Title]] wiki links."""
    base = os.path.dirname(rel)

    def repl(m):
        target = os.path.normpath(os.path.join(base, m.group(2))).replace(os.sep, "/")
        title = PAGES.get(target)
        return f"[[{title}]]" if title else m.group(0)

    return MD_LINK.sub(repl, content)


def load_config():
    with open(os.path.join(ROOT, ".mcp.json"), encoding="utf-8") as f:
        server = json.load(f)["mcpServers"]["chilladesk"]
    token = os.environ.get("CHILLADESK_TOKEN")
    auth = f"Bearer {token}" if token else server["headers"]["Authorization"]
    return server["url"], auth


def call(url, auth, tool, args):
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": tool, "arguments": args}}).encode()
    req = urllib.request.Request(url, data=body, headers={
        "Authorization": auth,
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    })
    with urllib.request.urlopen(req, timeout=60) as resp:
        reply = json.load(resp)
    if "error" in reply:
        raise RuntimeError(f"{tool}: {reply['error']}")
    result = reply["result"]
    text = "".join(c.get("text", "") for c in result.get("content", []))
    if result.get("isError"):
        raise RuntimeError(f"{tool}: {text}")
    return text


def main():
    dry = "--dry-run" in sys.argv
    url, auth = load_config()
    existing = json.loads(call(url, auth, "list_wiki_pages", {"project": PROJECT}))
    titles = {p["title"].lower() for p in (existing if isinstance(existing, list) else existing.get("pages", []))}
    for rel, title in PAGES.items():
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            print(f"skip  {rel} (missing)")
            continue
        with open(path, encoding="utf-8") as f:
            content = to_wiki_links(rel, f.read())
        action = "update" if title.lower() in titles else "create"
        print(f"{action:6} {title} <- {rel} ({len(content)} chars)")
        if dry:
            continue
        if action == "update":
            call(url, auth, "update_wiki_page", {"project": PROJECT, "title": title, "content": content})
        else:
            args = {"project": PROJECT, "title": title, "content": content}
            if title != PARENT:
                args["parent"] = PARENT
            call(url, auth, "create_wiki_page", args)


if __name__ == "__main__":
    main()
