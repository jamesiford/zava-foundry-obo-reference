"""Render the interactive diagrams to PNGs (for the deck and docs).

    python render_diagrams.py            # rewrites the Zava-Compass-OBO-*.png files next to this script

Serves this folder over a local http server (the HTML loads fine from file:// too, but a server lets us
disable caching) and drives headless Chromium via Playwright.
"""
import asyncio, http.server, pathlib, socketserver, threading
from playwright.async_api import async_playwright

HERE = pathlib.Path(__file__).parent
ARCH, SEQ = "Zava-Compass-OBO-Architecture.html", "Zava-Compass-OBO-Sequence.html"

# (output name, html file, query string, viewport height)
JOBS = [
    ("Zava-Compass-OBO-Architecture_L1", ARCH, "view=context", 1620),
    ("Zava-Compass-OBO-Architecture_L2", ARCH, "view=containers", 1620),
    ("Zava-Compass-OBO-Architecture_L3", ARCH, "view=components", 1620),
    ("Zava-Compass-OBO-Sequence", SEQ, "x=1", 2000),
]


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=str(HERE), **k)

    def log_message(self, *a):
        pass

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


async def main():
    srv = socketserver.TCPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    port = srv.server_address[1]
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={"width": 2880, "height": 1620})
        for name, file, query, height in JOBS:
            await page.set_viewport_size({"width": 2880, "height": height})
            await page.goto(f"http://127.0.0.1:{port}/{file}?{query}&chrome=0&scoutTheme=light")
            await page.wait_for_timeout(900)
            await page.screenshot(path=str(HERE / f"{name}.png"), full_page=False)
            print("wrote", name)
        await browser.close()
    srv.shutdown()


asyncio.run(main())

