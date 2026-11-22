#!/usr/bin/env python3
"""Snapshot existing product artwork into the native Station client.

Run with the existing branding virtual environment. Original SVGs are retained;
PNG resources are rendered for WPF with their original aspect ratios/colors.
"""
from pathlib import Path
import hashlib
import json
import re
import cairosvg

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = ROOT.parent
DEST = ROOT / "parallax/station/windows/Assets/Brands"


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    products = [
        ("slipstream", "Slipstream", "Work orchestration", "https://slipstream.codes", "#8cd9ad", "marketing/public/assets/logos/slipstream-mark.svg", "marketing/public/assets/logos/slipstream-text.svg", False),
        ("plexus", "Plexus", "People and teams", "https://plexus.team", "#7bc9ed", "marketing/public/assets/logos/plexus-mark.svg", "marketing/public/assets/logos/plexus-text.svg", False),
        ("positronic", "Positronic", "Intelligence and infrastructure", "https://positronic.app", "#f4cd6d", "marketing/public/assets/logos/positronic-mark.svg", "marketing/public/assets/logos/positronic-text.svg", False),
        ("nexus", "Nexus", "Identity and organization", "https://my.parallaxweb.services", "#f26cff", "nexus/public/assets/nexus/mark.svg", "nexus/public/assets/nexus/text.svg", True),
        ("nebula", "NEBULA", "Networks and clusters", "https://nebula.parallax.link/dashboard", "#a78bfa", "nucleus/apps/nebula-cpanel/payload/shared/nebula.svg", None, False),
        ("nucleus", "nucleus.", "Machines and private fabric", "https://nucleus.parallax.link/dashboard", "#75e1bb", "nucleus/apps/resource-map/index.html#brand-svg", None, False),
    ]
    provenance = []

    def snapshot(source, name, wordmark=False, inherited_color=None):
        if source.endswith("#brand-svg"):
            html = (WORKSPACE / source.split("#")[0]).read_text()
            match = re.search(r'<a[^>]*class="brand"[^>]*>(<svg.*?</svg>)', html, re.S)
            if not match:
                raise ValueError("Nucleus resource-map brand SVG not found")
            data = match[1].encode()
        else:
            data = (WORKSPACE / source).read_bytes()
        (DEST / (name + ".svg")).write_bytes(data)
        rendered = data
        if inherited_color:
            rendered = data.replace(b"<svg", ('<svg color="' + inherited_color + '"').encode(), 1)
        png = DEST / (name + ".png")
        if wordmark:
            cairosvg.svg2png(bytestring=rendered, write_to=str(png), output_width=640)
        else:
            cairosvg.svg2png(bytestring=rendered, write_to=str(png), output_width=192)
        provenance.append({"source": source, "svg": name + ".svg", "sha256": hashlib.sha256(data).hexdigest(), "png": png.name, "inheritedColor": inherited_color})
        return "Assets/Brands/" + png.name

    catalog = []
    for key, name, role, url, accent, mark, wordmark, light in products:
        mark_resource = snapshot(mark, key + "-mark", inherited_color=accent if key == "nucleus" else None)
        wordmark_resource = snapshot(wordmark, key + "-wordmark", True) if wordmark else None
        catalog.append(dict(key=key, name=name, role=role, url=url, accent=accent,
                            mark=mark_resource, wordmark=wordmark_resource, wordmarkOnLight=light))
    snapshot("marketing/public/assets/parallax-header-wordmark.svg", "parallax-wordmark", True)
    (DEST / "catalog.json").write_text(json.dumps(catalog, indent=2) + "\n")
    (DEST / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(f"Preserved {len(provenance)} original SVG assets for {len(catalog)} product brands and Parallax.")


if __name__ == "__main__":
    main()
