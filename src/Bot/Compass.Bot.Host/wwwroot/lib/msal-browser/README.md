# msal-browser (vendored)

The web front door (`wwwroot/index.html`) signs people in with MSAL.js. The browser bundle is checked in rather than loaded
from a CDN so the page's Content-Security-Policy can stay `script-src 'self'` and a CDN outage or tamper cannot reach a
page that holds a bearer token.

| | |
|---|---|
| Package | `@azure/msal-browser` |
| Version | **4.30.0** (npm `lts` dist-tag; v5 was not used because it changes the redirect/popup bridge model) |
| File | `msal-browser.min.js` (from the package's `lib/` browser bundle) |
| SHA256 | `2B580165064A6A4EC041EBD44832299172688A23A42C3B81D65314799DBA04EB` |
| Licence | MIT, see `LICENSE` |

To update: download the new tarball with `npm pack @azure/msal-browser@<version>`, replace the file, and update the
version and hash above. `.gitattributes` marks this folder `-text` so the bytes (and therefore the hash) are preserved.
