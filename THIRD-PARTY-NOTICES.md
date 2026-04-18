# Third-party notices

KSPBridge is licensed under the MIT License (see [LICENSE](LICENSE)).

The compiled mod redistributes the following third-party libraries, each
under its own license:

---

## MQTTnet

- **Version:** 4.3.7.1207
- **License:** MIT
- **Upstream:** https://github.com/dotnet/MQTTnet
- **Bundled binaries:** `GameData/KSPBridge/Plugins/MQTTnet.dll`,
  `GameData/KSPBridge/Plugins/MQTTnet.Extensions.ManagedClient.dll`

```
The MIT License (MIT)

Copyright (c) 2016-2024 The contributors of MQTTnet

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Three.js (r128)

- **License:** MIT
- **Upstream:** https://github.com/mrdoob/three.js
- **Bundled binary:** `consoles/hard-scifi/lib/three.min.js`
- Copyright (c) 2010-2021 three.js authors. MIT terms apply (see above).

---

## MQTT.js (4.3.7)

- **License:** MIT
- **Upstream:** https://github.com/mqttjs/MQTT.js
- **Bundled binary:** `consoles/hard-scifi/lib/mqtt.min.js`
- Copyright (c) 2015-2016 MQTT.js contributors; Copyright 2011-2014 Adam Rudd.
  MIT terms apply (see above).

---

## topojson-client (3.x)

- **License:** ISC (functionally equivalent to MIT for redistribution)
- **Upstream:** https://github.com/topojson/topojson-client
- **Bundled binary:** `consoles/hard-scifi/lib/topojson-client.min.js`
- Copyright (c) Mike Bostock. Permission to use, copy, modify, and/or
  distribute granted with copyright notice retained.

---

## Hard-scifi console (HTML/CSS/JS)

- **License:** MIT
- **Upstream:** https://github.com/johnmknight/KSA-Bridge (`examples/hard-scifi/`)
- **Bundled file:** `consoles/hard-scifi/hardscifi-fdo-console.html`
- Adapted from KSA-Bridge by John M. Knight (same author). Patched to
  consume `ksp/telemetry/*` topics and connect to the homelab broker over
  WebSocket on port 9002.

---

## Planet topology data (NASA / USGS / public domain)

- The contents of `consoles/hard-scifi/data/` (Mars craters, Moon mare,
  Jupiter bands, etc.) are derived from public-domain NASA / USGS
  cartographic datasets via KSA-Bridge's `DATA_SOURCES.md`. See that
  document for full attribution.
