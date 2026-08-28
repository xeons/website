/*
 * The developer tools under /tools.
 *
 * Everything here runs in the browser and nothing is sent to the server, which is the point
 * of the page: text pasted into a hash or a JWT decoder should not travel.
 *
 * Blazor's enhanced navigation swaps the DOM in place, so the listeners are bound once on
 * document and work by delegation. Per-panel setup runs from activate(), which is called
 * again after every navigation and skips panels it has already seen.
 */
(function () {
    'use strict';

    var encoder = new TextEncoder();
    var decoder = new TextDecoder();

    var DEBOUNCE_MS = 120;
    var REGEX_TIMEOUT_MS = 1000;
    var MAX_DIFF_CELLS = 4000000;

    /* ------------------------------------------------------------------ bytes -- */

    function toBytes(text) {
        return encoder.encode(text);
    }

    function fromBytes(bytes) {
        return decoder.decode(bytes);
    }

    function bytesToBase64(bytes) {
        var binary = '';
        var chunk = 0x8000;

        /* Chunked: apply() on a very large array overflows the argument list. */
        for (var i = 0; i < bytes.length; i += chunk) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        }

        return btoa(binary);
    }

    function base64ToBytes(text) {
        var binary = atob(text);
        var out = new Uint8Array(binary.length);

        for (var i = 0; i < binary.length; i++) {
            out[i] = binary.charCodeAt(i);
        }

        return out;
    }

    /* Accepts either alphabet and restores the padding atob insists on. */
    function normaliseBase64(text) {
        var clean = text.replace(/\s+/g, '').replace(/-/g, '+').replace(/_/g, '/');
        var remainder = clean.length % 4;

        if (remainder === 1) {
            throw new Error('That is not a valid Base64 length.');
        }

        if (remainder) {
            clean += new Array(5 - remainder).join('=');
        }

        return clean;
    }

    function bytesToHex(bytes, separator, upper) {
        var parts = [];

        for (var i = 0; i < bytes.length; i++) {
            var digits = bytes[i].toString(16);
            parts.push(digits.length === 1 ? '0' + digits : digits);
        }

        var joined = parts.join(separator || '');
        return upper ? joined.toUpperCase() : joined;
    }

    function hexToBytes(text) {
        var clean = text.replace(/0x/gi, '').replace(/[\s:,\-]/g, '');

        if (!clean) return new Uint8Array(0);

        if (!/^[0-9a-f]+$/i.test(clean)) {
            throw new Error('That contains characters which are not hex digits.');
        }

        if (clean.length % 2) {
            throw new Error('Hex needs an even number of digits; that has ' + clean.length + '.');
        }

        var out = new Uint8Array(clean.length / 2);

        for (var i = 0; i < out.length; i++) {
            out[i] = parseInt(clean.substr(i * 2, 2), 16);
        }

        return out;
    }

    function bytesToBinary(bytes, spaced) {
        var parts = [];

        for (var i = 0; i < bytes.length; i++) {
            parts.push(('00000000' + bytes[i].toString(2)).slice(-8));
        }

        return parts.join(spaced ? ' ' : '');
    }

    function binaryToBytes(text) {
        var clean = text.replace(/[^01]/g, '');

        if (!clean) return new Uint8Array(0);

        if (clean.length % 8) {
            throw new Error('Binary needs a multiple of eight bits; that has ' + clean.length + '.');
        }

        var out = new Uint8Array(clean.length / 8);

        for (var i = 0; i < out.length; i++) {
            out[i] = parseInt(clean.substr(i * 8, 8), 2);
        }

        return out;
    }

    /* ----------------------------------------------------------------- crypto -- */

    function subtle() {
        return (window.crypto && window.crypto.subtle) || null;
    }

    var NO_SUBTLE =
        'The browser only exposes its crypto API over HTTPS or on localhost, so this cannot '
        + 'run on the current connection.';

    function digest(algorithm, bytes) {
        var api = subtle();

        if (!api) return Promise.reject(new Error(NO_SUBTLE));

        return api.digest(algorithm, bytes).then(function (buffer) {
            return new Uint8Array(buffer);
        });
    }

    function randomBytes(count) {
        var bytes = new Uint8Array(count);

        if (!window.crypto || !window.crypto.getRandomValues) {
            throw new Error('This browser does not expose a secure random source.');
        }

        window.crypto.getRandomValues(bytes);
        return bytes;
    }

    function rotateLeft(value, shift) {
        return (value << shift) | (value >>> (32 - shift));
    }

    /*
     * MD5, per RFC 1321. Web Crypto deliberately omits it, and it is still what a great many
     * checksums and legacy systems quote.
     */
    var MD5_SHIFTS = [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
    ];

    var MD5_SINES = (function () {
        var table = new Int32Array(64);

        for (var i = 0; i < 64; i++) {
            table[i] = Math.floor(Math.abs(Math.sin(i + 1)) * 4294967296) | 0;
        }

        return table;
    })();

    function md5(bytes) {
        var length = bytes.length;
        var padded = new Uint8Array((((length + 8) >> 6) + 1) << 6);

        padded.set(bytes);
        padded[length] = 0x80;

        var view = new DataView(padded.buffer);
        var bits = length * 8;

        view.setUint32(padded.length - 8, bits >>> 0, true);
        view.setUint32(padded.length - 4, Math.floor(bits / 4294967296), true);

        var a = 0x67452301;
        var b = 0xefcdab89;
        var c = 0x98badcfe;
        var d = 0x10325476;
        var block = new Int32Array(16);

        for (var offset = 0; offset < padded.length; offset += 64) {
            for (var j = 0; j < 16; j++) {
                block[j] = view.getUint32(offset + j * 4, true);
            }

            var wa = a;
            var wb = b;
            var wc = c;
            var wd = d;

            for (var i = 0; i < 64; i++) {
                var f;
                var g;

                if (i < 16) {
                    f = (wb & wc) | (~wb & wd);
                    g = i;
                } else if (i < 32) {
                    f = (wd & wb) | (~wd & wc);
                    g = (5 * i + 1) % 16;
                } else if (i < 48) {
                    f = wb ^ wc ^ wd;
                    g = (3 * i + 5) % 16;
                } else {
                    f = wc ^ (wb | ~wd);
                    g = (7 * i) % 16;
                }

                var rotated = wd;
                wd = wc;
                wc = wb;
                wb = (wb + rotateLeft((wa + f + MD5_SINES[i] + block[g]) | 0, MD5_SHIFTS[i])) | 0;
                wa = rotated;
            }

            a = (a + wa) | 0;
            b = (b + wb) | 0;
            c = (c + wc) | 0;
            d = (d + wd) | 0;
        }

        var out = new Uint8Array(16);
        var outView = new DataView(out.buffer);

        outView.setInt32(0, a, true);
        outView.setInt32(4, b, true);
        outView.setInt32(8, c, true);
        outView.setInt32(12, d, true);

        return out;
    }

    var crcTable = null;

    function crc32(bytes) {
        if (!crcTable) {
            crcTable = new Int32Array(256);

            for (var n = 0; n < 256; n++) {
                var value = n;

                for (var k = 0; k < 8; k++) {
                    value = (value & 1) ? (0xedb88320 ^ (value >>> 1)) : (value >>> 1);
                }

                crcTable[n] = value;
            }
        }

        var crc = -1;

        for (var i = 0; i < bytes.length; i++) {
            crc = (crc >>> 8) ^ crcTable[(crc ^ bytes[i]) & 0xff];
        }

        var out = new Uint8Array(4);
        new DataView(out.buffer).setUint32(0, (crc ^ -1) >>> 0, false);

        return out;
    }

    /* ------------------------------------------------------------------- DOM -- */

    function clearNode(node) {
        if (!node) return;

        while (node.firstChild) {
            node.removeChild(node.firstChild);
        }
    }

    function make(tag, className, text) {
        var node = document.createElement(tag);

        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = String(text);

        return node;
    }

    function setStatus(panel, text) {
        var node = panel.querySelector('[data-tool-status]');

        if (node) node.textContent = text || '';
        panel.classList.toggle('has-error', !!text);
    }

    function readOptions(panel) {
        var options = {};

        panel.querySelectorAll('[data-tool-option]').forEach(function (node) {
            var key = node.getAttribute('data-tool-option');
            options[key] = node.type === 'checkbox' ? node.checked : node.value;
        });

        return options;
    }

    function inputValue(panel, selector) {
        var node = panel.querySelector(selector);
        return node ? node.value : '';
    }

    function clamp(value, low, high) {
        if (isNaN(value)) return low;
        return Math.min(high, Math.max(low, value));
    }

    function plural(count, word) {
        if (count === 1) return count + ' ' + word;

        /* A sibilant ending takes -es: "match" becomes "matches", not "matchs". */
        var suffix = /(s|x|z|ch|sh)$/.test(word) ? 'es' : 's';

        return count + ' ' + word + suffix;
    }

    /* ------------------------------------------------------- transform tools -- */

    var MARKUP_ENTITIES = {
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    };

    function encodeEntities(text, everything) {
        var escaped = text.replace(/[&<>"']/g, function (character) {
            return MARKUP_ENTITIES[character];
        });

        if (!everything) return escaped;

        return escaped.replace(/[^\x20-\x7e\n\r\t]/gu, function (character) {
            return '&#' + character.codePointAt(0) + ';';
        });
    }

    /*
     * A textarea's content model is RCDATA, so assigning to innerHTML resolves entities
     * without ever building an element out of the input.
     */
    function decodeEntities(text) {
        var scratch = document.createElement('textarea');
        scratch.innerHTML = text;
        return scratch.value;
    }

    function sortJsonKeys(value) {
        if (Array.isArray(value)) return value.map(sortJsonKeys);

        if (value && typeof value === 'object') {
            var sorted = {};

            Object.keys(value).sort().forEach(function (key) {
                sorted[key] = sortJsonKeys(value[key]);
            });

            return sorted;
        }

        return value;
    }

    /*
     * Browsers report a character offset; a line and column is what a person can act on.
     * Some versions already give both, and some give neither, so the message is only added to
     * when it carries an offset and no line of its own.
     */
    function parseJson(text) {
        try {
            return JSON.parse(text);
        } catch (error) {
            var message = error.message || 'That is not valid JSON.';
            var found = /position (\d+)/i.exec(message);

            if (!found || /line \d+/i.test(message)) throw new Error(message);

            var position = parseInt(found[1], 10);
            var before = text.slice(0, position);
            var line = before.split('\n').length;
            var column = position - before.lastIndexOf('\n');

            throw new Error(message + ' (line ' + line + ', column ' + column + ')');
        }
    }

    function describeJson(value) {
        if (Array.isArray(value)) {
            return 'Valid JSON: an array of ' + plural(value.length, 'item') + '.';
        }

        if (value && typeof value === 'object') {
            return 'Valid JSON: an object with ' + plural(Object.keys(value).length, 'key') + '.';
        }

        return 'Valid JSON: a single ' + (value === null ? 'null' : typeof value) + ' value.';
    }

    var TRANSFORMS = {
        base64: function (action, text, options) {
            if (action === 'decode') {
                return fromBytes(base64ToBytes(normaliseBase64(text)));
            }

            var encoded = bytesToBase64(toBytes(text));

            return options.urlsafe
                ? encoded.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
                : encoded;
        },

        url: function (action, text, options) {
            if (action === 'decode') {
                return decodeURIComponent(options.plus ? text.replace(/\+/g, ' ') : text);
            }

            return options.mode === 'uri' ? encodeURI(text) : encodeURIComponent(text);
        },

        hex: function (action, text, options) {
            if (action === 'decode') return fromBytes(hexToBytes(text));
            return bytesToHex(toBytes(text), options.separator, options.upper);
        },

        binary: function (action, text, options) {
            if (action === 'decode') return fromBytes(binaryToBytes(text));
            return bytesToBinary(toBytes(text), options.spaced);
        },

        entities: function (action, text, options) {
            if (action === 'decode') return decodeEntities(text);
            return encodeEntities(text, options.scope === 'all');
        },

        json: function (action, text, options) {
            var parsed = parseJson(text);

            if (action === 'validate') return describeJson(parsed);

            var value = options.sort ? sortJsonKeys(parsed) : parsed;

            if (action === 'minify') return JSON.stringify(value);

            var indent = options.indent === 'tab' ? '\t' : (parseInt(options.indent, 10) || 2);
            return JSON.stringify(value, null, indent);
        },

        hmac: function (action, text, options) {
            var api = subtle();

            if (!api) return Promise.reject(new Error(NO_SUBTLE));
            if (!options.key) throw new Error('Enter a secret key.');

            var algorithm = options.algorithm || 'SHA-256';

            return api
                .importKey('raw', toBytes(options.key), { name: 'HMAC', hash: algorithm }, false, ['sign'])
                .then(function (key) {
                    return api.sign('HMAC', key, toBytes(text));
                })
                .then(function (buffer) {
                    var bytes = new Uint8Array(buffer);

                    return options.encoding === 'base64'
                        ? bytesToBase64(bytes)
                        : bytesToHex(bytes, '', false);
                });
        }
    };

    function runTransform(panel, action) {
        var transform = TRANSFORMS[panel.getAttribute('data-tool')];

        if (!transform || !action) return;

        var output = panel.querySelector('[data-tool-output]');
        var text = inputValue(panel, '[data-tool-input]');

        if (!text) {
            if (output) output.value = '';
            setStatus(panel, '');
            return;
        }

        var result;

        try {
            result = transform(action, text, readOptions(panel));
        } catch (error) {
            failed(panel, output, error);
            return;
        }

        var token = nextToken(panel);

        Promise.resolve(result).then(function (value) {
            if (token !== panel.toolToken) return;

            if (output) output.value = value;
            setStatus(panel, '');
        }, function (error) {
            if (token !== panel.toolToken) return;
            failed(panel, output, error);
        });
    }

    function failed(panel, output, error) {
        if (output) output.value = '';
        setStatus(panel, (error && error.message) || 'That did not work.');
    }

    /* Guards against a slow result overwriting a newer one after fast typing. */
    function nextToken(panel) {
        panel.toolToken = (panel.toolToken || 0) + 1;
        return panel.toolToken;
    }

    /* ------------------------------------------------------------ hash table -- */

    var SHA_NAMES = {
        sha1: 'SHA-1',
        sha256: 'SHA-256',
        sha384: 'SHA-384',
        sha512: 'SHA-512'
    };

    function digestFor(name, bytes) {
        if (name === 'md5') return Promise.resolve(md5(bytes));
        if (name === 'crc32') return Promise.resolve(crc32(bytes));
        return digest(SHA_NAMES[name], bytes);
    }

    function hashUpdate(panel) {
        var text = inputValue(panel, '[data-tool-input]');
        var options = readOptions(panel);
        var bytes = toBytes(text);
        var token = nextToken(panel);

        setStatus(panel, '');

        panel.querySelectorAll('[data-hash]').forEach(function (row) {
            var cell = row.querySelector('[data-copy-text]');

            if (!cell) return;

            if (!text) {
                cell.textContent = '';
                return;
            }

            digestFor(row.getAttribute('data-hash'), bytes).then(function (result) {
                if (token !== panel.toolToken) return;

                cell.textContent = options.encoding === 'base64'
                    ? bytesToBase64(result)
                    : bytesToHex(result, '', !!options.upper);
            }, function (error) {
                if (token !== panel.toolToken) return;
                cell.textContent = '';
                setStatus(panel, error.message);
            });
        });
    }

    /* ------------------------------------------------------------------- JWT -- */

    var JWT_TIMES = { exp: 'Expires', iat: 'Issued at', nbf: 'Not valid before', auth_time: 'Authenticated at' };

    function jwtSection(title, body) {
        var wrapper = make('div', 'tool-block');

        wrapper.appendChild(make('h3', null, title));
        wrapper.appendChild(make('pre', 'tool-pre', body));

        return wrapper;
    }

    function jwtUpdate(panel) {
        var result = panel.querySelector('[data-tool-result]');
        var token = inputValue(panel, '[data-tool-input]').trim();

        clearNode(result);

        if (!token) {
            setStatus(panel, '');
            return;
        }

        var parts = token.split('.');

        if (parts.length !== 3) {
            setStatus(panel,
                'A JWT has three sections separated by dots; that has ' + parts.length + '.');
            return;
        }

        var header;
        var payload;

        try {
            header = JSON.parse(fromBytes(base64ToBytes(normaliseBase64(parts[0]))));
            payload = JSON.parse(fromBytes(base64ToBytes(normaliseBase64(parts[1]))));
        } catch (error) {
            setStatus(panel, 'The header or the payload is not valid Base64url-encoded JSON.');
            return;
        }

        setStatus(panel, '');

        result.appendChild(jwtSection('Header', JSON.stringify(header, null, 2)));
        result.appendChild(jwtSection('Payload', JSON.stringify(payload, null, 2)));

        var claims = jwtClaims(payload);
        if (claims) result.appendChild(claims);

        result.appendChild(jwtSection('Signature (not verified)', parts[2]));
    }

    function jwtClaims(payload) {
        var rows = [];

        Object.keys(JWT_TIMES).forEach(function (claim) {
            if (typeof payload[claim] !== 'number') return;

            var when = new Date(payload[claim] * 1000);
            rows.push([JWT_TIMES[claim] + ' (' + claim + ')', when.toISOString() + ' - ' + relative(when)]);
        });

        if (typeof payload.exp === 'number') {
            var expired = payload.exp * 1000 < Date.now();
            rows.push(['Status', expired ? 'Expired' : 'Within its lifetime']);
        }

        if (rows.length === 0) return null;

        var wrapper = make('div', 'tool-block');
        wrapper.appendChild(make('h3', null, 'Timestamps'));

        var table = make('table', 'tool-table');
        var body = make('tbody');

        rows.forEach(function (row) {
            var tr = make('tr');
            var th = make('th', null, row[0]);

            th.setAttribute('scope', 'row');
            tr.appendChild(th);
            tr.appendChild(make('td', null, row[1]));
            body.appendChild(tr);
        });

        table.appendChild(body);

        var scroller = make('div', 'tool-table-wrap');
        scroller.appendChild(table);
        wrapper.appendChild(scroller);

        return wrapper;
    }

    function relative(date) {
        var seconds = Math.round((date.getTime() - Date.now()) / 1000);
        var past = seconds < 0;
        var amount = Math.abs(seconds);
        var units = [['year', 31536000], ['day', 86400], ['hour', 3600], ['minute', 60], ['second', 1]];

        for (var i = 0; i < units.length; i++) {
            var size = units[i][1];

            if (amount >= size || size === 1) {
                var count = Math.round(amount / size);
                return past ? plural(count, units[i][0]) + ' ago' : 'in ' + plural(count, units[i][0]);
            }
        }

        return 'now';
    }

    /* ---------------------------------------------------------- base convert -- */

    var MAX_DIGITS = 4096;

    function parseInBase(digits, base) {
        var alphabet = '0123456789abcdef'.slice(0, base);
        var allowed = new RegExp('^[' + alphabet + ']+$', 'i');

        if (!allowed.test(digits)) {
            throw new Error('That is not a valid base ' + base + ' number.');
        }

        /* Accumulating a BigInt costs time quadratic in the digit count. */
        if (digits.length > MAX_DIGITS) {
            throw new Error(
                'That is ' + digits.length + ' digits. Anything past ' + MAX_DIGITS
                + ' takes long enough to convert that the page would stop responding.');
        }

        var value = BigInt(0);
        var radix = BigInt(base);

        for (var i = 0; i < digits.length; i++) {
            value = value * radix + BigInt(parseInt(digits.charAt(i), base));
        }

        return value;
    }

    function baseUpdate(panel, source) {
        if (!source || !source.hasAttribute('data-base')) return;

        var base = parseInt(source.getAttribute('data-base'), 10);
        var raw = source.value.trim().replace(/[\s_,]/g, '');
        var others = [];

        panel.querySelectorAll('[data-base]').forEach(function (field) {
            if (field !== source) others.push(field);
        });

        if (!raw) {
            others.forEach(function (field) { field.value = ''; });
            setStatus(panel, '');
            return;
        }

        var negative = raw.charAt(0) === '-';
        var digits = (negative ? raw.slice(1) : raw).replace(/^0[bxo]/i, '');
        var value;

        try {
            value = parseInBase(digits, base);
        } catch (error) {
            others.forEach(function (field) { field.value = ''; });
            setStatus(panel, error.message);
            return;
        }

        setStatus(panel, '');

        others.forEach(function (field) {
            var target = parseInt(field.getAttribute('data-base'), 10);
            field.value = (negative ? '-' : '') + value.toString(target);
        });
    }

    /* ------------------------------------------------------------- timestamp -- */

    function pad(value, width) {
        return ('0000' + value).slice(-(width || 2));
    }

    function localInputValue(date) {
        return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
            + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds());
    }

    function timestampWrite(panel, milliseconds) {
        var outputs = {
            seconds: String(Math.floor(milliseconds / 1000)),
            millis: String(milliseconds),
            iso: new Date(milliseconds).toISOString(),
            utc: new Date(milliseconds).toUTCString(),
            local: new Date(milliseconds).toString(),
            relative: relative(new Date(milliseconds))
        };

        panel.querySelectorAll('[data-ts-out]').forEach(function (node) {
            node.textContent = outputs[node.getAttribute('data-ts-out')] || '';
        });
    }

    function timestampClear(panel) {
        panel.querySelectorAll('[data-ts-out]').forEach(function (node) {
            node.textContent = '';
        });

        var hint = panel.querySelector('[data-ts="unit"]');
        if (hint) hint.textContent = '';
    }

    function timestampFromEpoch(panel, raw, syncDate) {
        var text = String(raw).trim();
        var hint = panel.querySelector('[data-ts="unit"]');

        if (!text) {
            timestampClear(panel);
            setStatus(panel, '');
            return;
        }

        if (!/^-?\d+$/.test(text)) {
            timestampClear(panel);
            setStatus(panel, 'A Unix timestamp is a whole number of seconds or milliseconds.');
            return;
        }

        var number = Number(text);

        /* Anything past ten digits is milliseconds; a seconds value that large is year 5138. */
        var isMillis = Math.abs(number) > 99999999999;
        var milliseconds = isMillis ? number : number * 1000;

        if (!isFinite(milliseconds) || Math.abs(milliseconds) > 8640000000000000) {
            timestampClear(panel);
            setStatus(panel, 'That is outside the range a date can represent.');
            return;
        }

        if (hint) hint.textContent = 'Read as ' + (isMillis ? 'milliseconds' : 'seconds') + '.';

        setStatus(panel, '');
        timestampWrite(panel, milliseconds);

        if (syncDate) {
            var field = panel.querySelector('[data-ts="date"]');
            if (field) field.value = localInputValue(new Date(milliseconds));
        }
    }

    function timestampFromDate(panel, raw) {
        if (!raw) {
            timestampClear(panel);
            setStatus(panel, '');
            return;
        }

        var parsed = new Date(raw);

        if (isNaN(parsed.getTime())) {
            setStatus(panel, 'That is not a date this browser can read.');
            return;
        }

        setStatus(panel, '');
        timestampWrite(panel, parsed.getTime());

        var epochField = panel.querySelector('[data-ts="epoch"]');
        if (epochField) epochField.value = String(Math.floor(parsed.getTime() / 1000));

        var hint = panel.querySelector('[data-ts="unit"]');
        if (hint) hint.textContent = 'Read as seconds.';
    }

    function timestampUpdate(panel, source) {
        if (!source) return;

        var role = source.getAttribute('data-ts');

        if (role === 'epoch') timestampFromEpoch(panel, source.value, true);
        else if (role === 'date') timestampFromDate(panel, source.value);
    }

    function timestampNow(panel) {
        var epochField = panel.querySelector('[data-ts="epoch"]');
        var seconds = Math.floor(Date.now() / 1000);

        if (epochField) epochField.value = String(seconds);
        timestampFromEpoch(panel, seconds, true);
    }

    /* ---------------------------------------------------------------- colour -- */

    function parseColour(text) {
        var value = text.trim().toLowerCase();
        var hex = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/.exec(value);

        if (hex) {
            var digits = hex[1];

            if (digits.length === 3) {
                digits = digits.charAt(0) + digits.charAt(0)
                    + digits.charAt(1) + digits.charAt(1)
                    + digits.charAt(2) + digits.charAt(2);
            }

            return {
                r: parseInt(digits.substr(0, 2), 16),
                g: parseInt(digits.substr(2, 2), 16),
                b: parseInt(digits.substr(4, 2), 16)
            };
        }

        var rgb = /^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/.exec(value);

        if (rgb) {
            return {
                r: clamp(parseInt(rgb[1], 10), 0, 255),
                g: clamp(parseInt(rgb[2], 10), 0, 255),
                b: clamp(parseInt(rgb[3], 10), 0, 255)
            };
        }

        var hsl = /^hsla?\(\s*(-?[\d.]+)[\s,]+([\d.]+)%?[\s,]+([\d.]+)%?/.exec(value);

        if (hsl) {
            return hslToRgb(parseFloat(hsl[1]), parseFloat(hsl[2]) / 100, parseFloat(hsl[3]) / 100);
        }

        return null;
    }

    function hslToRgb(hue, saturation, lightness) {
        var h = ((hue % 360) + 360) % 360 / 360;
        var s = clamp(saturation, 0, 1);
        var l = clamp(lightness, 0, 1);

        if (s === 0) {
            var grey = Math.round(l * 255);
            return { r: grey, g: grey, b: grey };
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return {
            r: Math.round(hueToChannel(p, q, h + 1 / 3) * 255),
            g: Math.round(hueToChannel(p, q, h) * 255),
            b: Math.round(hueToChannel(p, q, h - 1 / 3) * 255)
        };
    }

    function hueToChannel(p, q, t) {
        var value = t;

        if (value < 0) value += 1;
        if (value > 1) value -= 1;
        if (value < 1 / 6) return p + (q - p) * 6 * value;
        if (value < 1 / 2) return q;
        if (value < 2 / 3) return p + (q - p) * (2 / 3 - value) * 6;

        return p;
    }

    function rgbToHsl(colour) {
        var r = colour.r / 255;
        var g = colour.g / 255;
        var b = colour.b / 255;
        var max = Math.max(r, g, b);
        var min = Math.min(r, g, b);
        var lightness = (max + min) / 2;
        var hue = 0;
        var saturation = 0;

        if (max !== min) {
            var span = max - min;

            saturation = lightness > 0.5 ? span / (2 - max - min) : span / (max + min);

            if (max === r) hue = ((g - b) / span + (g < b ? 6 : 0)) * 60;
            else if (max === g) hue = ((b - r) / span + 2) * 60;
            else hue = ((r - g) / span + 4) * 60;
        }

        return {
            h: Math.round(hue),
            s: Math.round(saturation * 100),
            l: Math.round(lightness * 100)
        };
    }

    function toHex(colour) {
        return '#' + pad(colour.r.toString(16)) + pad(colour.g.toString(16)) + pad(colour.b.toString(16));
    }

    function colourWrite(panel, colour, source) {
        var hsl = rgbToHsl(colour);
        var values = {
            hex: toHex(colour),
            rgb: 'rgb(' + colour.r + ', ' + colour.g + ', ' + colour.b + ')',
            hsl: 'hsl(' + hsl.h + ', ' + hsl.s + '%, ' + hsl.l + '%)'
        };

        panel.querySelectorAll('[data-colour]').forEach(function (field) {
            if (field === source) return;
            field.value = values[field.getAttribute('data-colour')];
        });

        var swatch = panel.querySelector('[data-colour-swatch]');
        if (swatch) swatch.style.background = values.hex;

        var picker = panel.querySelector('[data-colour-picker]');
        if (picker && picker !== source) picker.value = values.hex;
    }

    function colourUpdate(panel, source) {
        if (!source) return;

        var isPicker = source.hasAttribute('data-colour-picker');

        if (!isPicker && !source.hasAttribute('data-colour')) return;

        var colour = parseColour(source.value);

        if (!colour) {
            setStatus(panel, 'That is not a colour I can read. Try #1e73be, rgb(30, 115, 190) or hsl(208, 73%, 43%).');
            return;
        }

        setStatus(panel, '');
        colourWrite(panel, colour, isPicker ? null : source);
    }

    function colourInit(panel) {
        var picker = panel.querySelector('[data-colour-picker]');
        colourWrite(panel, parseColour(picker ? picker.value : '#1e73be'), null);
    }

    /* ------------------------------------------------------------------ UUID -- */

    function formatUuid(bytes) {
        var hex = bytesToHex(bytes, '', false);

        return hex.substr(0, 8) + '-' + hex.substr(8, 4) + '-' + hex.substr(12, 4)
            + '-' + hex.substr(16, 4) + '-' + hex.substr(20, 12);
    }

    function uuidV4() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }

        var bytes = randomBytes(16);

        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;

        return formatUuid(bytes);
    }

    var v7Time = 0;
    var v7Sequence = 0;

    /*
     * The first six bytes are a big-endian millisecond timestamp, which is what makes a set of
     * these sort into the order they were created.
     *
     * The timestamp alone is not enough: several generated within one millisecond would share
     * it and then sort by their random bits, in no order at all. The twelve bits after the
     * version hold a counter for that case, as RFC 9562 describes. When the counter fills, the
     * timestamp borrows a millisecond from the future rather than repeating.
     */
    function uuidV7() {
        var bytes = randomBytes(16);
        var now = Date.now();

        if (now > v7Time) {
            v7Time = now;
            v7Sequence = 0;
        } else {
            v7Sequence++;

            if (v7Sequence > 0xfff) {
                v7Time++;
                v7Sequence = 0;
            }
        }

        bytes[0] = Math.floor(v7Time / 1099511627776) & 0xff;
        bytes[1] = Math.floor(v7Time / 4294967296) & 0xff;
        bytes[2] = (v7Time >>> 24) & 0xff;
        bytes[3] = (v7Time >>> 16) & 0xff;
        bytes[4] = (v7Time >>> 8) & 0xff;
        bytes[5] = v7Time & 0xff;
        bytes[6] = 0x70 | ((v7Sequence >>> 8) & 0x0f);
        bytes[7] = v7Sequence & 0xff;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;

        return formatUuid(bytes);
    }

    function uuidGenerate(panel) {
        var options = readOptions(panel);
        var count = clamp(parseInt(options.count, 10), 1, 500);
        var version = options.version === '7' ? 7 : 4;
        var list = [];

        try {
            for (var i = 0; i < count; i++) {
                var id = version === 7 ? uuidV7() : uuidV4();

                if (options.upper) id = id.toUpperCase();
                if (options.braces) id = '{' + id + '}';

                list.push(id);
            }
        } catch (error) {
            setStatus(panel, error.message);
            return;
        }

        var output = panel.querySelector('[data-tool-output]');
        if (output) output.value = list.join('\n');

        setStatus(panel, plural(count, 'UUID') + ' generated.');
    }

    /* -------------------------------------------------------------- password -- */

    var POOLS = {
        lower: 'abcdefghijklmnopqrstuvwxyz',
        upper: 'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
        digits: '0123456789',
        symbols: '!#$%&()*+,-./:;<=>?@[]^_{|}~'
    };

    var WORDS = ('able acid aged also area army away baby back ball band bank base bath bear '
        + 'beat been beer bell belt bend best bike bill bird blue boat body bone book boot born '
        + 'both bowl bulk burn bush busy cake call calm came camp card care cars case cash cast '
        + 'cell chat chef chip city club coal coat code cold come cook cool copy cord core corn '
        + 'cost crew crop dark data date dawn dead deal dear debt deck deep deer desk dial diet '
        + 'disc dish disk dome done door dose down draw drew drop drum dual duck dust duty each '
        + 'earn ease east easy edge edit else even ever exit face fact fade fail fair fall farm '
        + 'fast fate fear feed feel feet fell file fill film find fine fire firm fish five flag '
        + 'flat flew flow foam fold folk food foot ford form fort four free from fuel full fund '
        + 'gain game gate gave gear gift girl give glad glow goal goat gold golf gone good gray '
        + 'grew grid grip grow gulf hair half hall hand hang hard harm hate have hawk head heal '
        + 'heap hear heat held helm help herb hero hide high hill hint hire hold hole holy home '
        + 'hope horn host hour huge hunt hurt icon idea inch iron item jail jazz join joke jump '
        + 'junk just keen keep kept keys kick kind king kite knee knew know lace lack lady laid '
        + 'lake lamp land lane last late lawn lazy lead leaf leak lean leap left lend lens less '
        + 'life lift like limb lime line link lion list live load loan lock loft logo long look '
        + 'loop lord lose loss lost loud love luck lump lung mail main make male mall many maps '
        + 'mark mask mass mast mate math meal mean meat meet melt menu mesh mile milk mill mind '
        + 'mine mint miss mist mode mold mood moon more most move much must name near neat neck '
        + 'need news next nice node none noon norm nose note noun oath odds okay once only onto '
        + 'open oral oven over pace pack page paid pain pair pale palm park part pass past path '
        + 'peak pear peel pick pier pile pine pink pipe plan play plot plug plus poem poet pole '
        + 'poll pond pool poor pork port pose post pour pull pump pure push quit quiz race rack '
        + 'rail rain rank rare rate read real reef rely rent rest rice rich ride ring rise risk '
        + 'road rock role roll roof room root rope rose rule rush rust safe said sail salt same '
        + 'sand save scan seal seat seed seek seem seen self sell send sent ship shoe shop shot '
        + 'show side sign silk sing sink site size skin slot slow snap snow soap sock soft soil '
        + 'sold sole solo song soon sort soul soup spin spot star stay stem step stir stop such '
        + 'suit sure swim take tale talk tall tank tape task taxi team tear tech teen tell tend '
        + 'tent term test text than that them then they thin this tide tidy tile till time tiny '
        + 'tips tire toll tone took tool tore torn tour town trap tray tree trim trip true tube '
        + 'tune turn twin type unit upon urge used user vary vast verb very view vine visa void '
        + 'vote wage wait wake walk wall want ward warm warn wash wave weak wear week well went '
        + 'were west what when whom wide wife wild will wind wine wing wipe wire wise wish with '
        + 'wolf wood wool word wore work worm worn wrap yard yarn year your zero zone').split(' ');

    var DRAW_ROUNDS = 8;
    var DRAW_SLACK = 32;

    /*
     * Picks count entries from pool uniformly.
     *
     * Values at or above limit are discarded rather than folded in, because taking the
     * remainder of the whole 32-bit range would favour the start of any pool whose size does
     * not divide it, which is most of them. Discarded draws are skipped and the shortfall is
     * made up by a further round.
     *
     * The number of rounds is capped and each over-draws, so this cannot spin: at any pool
     * size the rejection rate is below one in ten million, and running out of rounds raises
     * an error rather than trying again.
     */
    function pickMany(pool, count) {
        var size = pool.length;
        var limit = Math.floor(4294967296 / size) * size;
        var chosen = [];
        var round = 0;

        while (chosen.length < count && round < DRAW_ROUNDS) {
            var batch = new Uint32Array(count - chosen.length + DRAW_SLACK);

            window.crypto.getRandomValues(batch);

            for (var i = 0; i < batch.length && chosen.length < count; i++) {
                if (batch[i] < limit) chosen.push(pool[batch[i] % size]);
            }

            round++;
        }

        if (chosen.length < count) {
            throw new Error('The browser did not return enough usable random values.');
        }

        return chosen;
    }

    function passwordPool(options) {
        var pool = '';

        if (options.lower) pool += POOLS.lower;
        if (options.upper) pool += POOLS.upper;
        if (options.digits) pool += POOLS.digits;
        if (options.symbols) pool += POOLS.symbols;

        if (options.ambiguous) pool = pool.replace(/[Il1O0]/g, '');

        return pool;
    }

    function passwordGenerate(panel) {
        var options = readOptions(panel);
        var count = clamp(parseInt(options.count, 10), 1, 100);
        var length = clamp(parseInt(options.length, 10), 4, 256);
        var passphrase = options.style === 'passphrase';
        var pool = passphrase ? WORDS : passwordPool(options).split('');
        var size = passphrase ? clamp(Math.round(length / 4), 4, 24) : length;

        if (pool.length === 0) {
            setStatus(panel, 'Pick at least one kind of character.');
            return;
        }

        var list = [];

        try {
            var drawn = pickMany(pool, count * size);

            for (var i = 0; i < count; i++) {
                list.push(drawn.slice(i * size, (i + 1) * size).join(passphrase ? '-' : ''));
            }
        } catch (error) {
            setStatus(panel, error.message);
            return;
        }

        var output = panel.querySelector('[data-tool-output]');
        if (output) output.value = list.join('\n');

        var entropy = Math.floor(size * Math.log(pool.length) / Math.log(2));
        var meter = panel.querySelector('[data-password-entropy]');

        if (meter) {
            meter.textContent = entropy + ' bits of entropy: '
                + size + (passphrase ? ' words' : ' characters') + ' from a pool of '
                + pool.length + '. ' + entropyVerdict(entropy);
        }

        setStatus(panel, '');
    }

    function entropyVerdict(bits) {
        if (bits < 50) return 'Weak: fine for something disposable, not for an account.';
        if (bits < 70) return 'Reasonable for an ordinary account.';
        if (bits < 100) return 'Strong.';
        return 'Far beyond anything brute force will reach.';
    }

    /* ------------------------------------------------------------------ case -- */

    function splitWords(line) {
        return line
            .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
            .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
            .split(/[^A-Za-z0-9]+/)
            .filter(Boolean);
    }

    var CASES = {
        camel: function (words) {
            return words.map(function (word, index) {
                return index === 0 ? word.toLowerCase() : capitalise(word);
            }).join('');
        },
        pascal: function (words) {
            return words.map(capitalise).join('');
        },
        snake: function (words) {
            return words.join('_').toLowerCase();
        },
        constant: function (words) {
            return words.join('_').toUpperCase();
        },
        kebab: function (words) {
            return words.join('-').toLowerCase();
        },
        dot: function (words) {
            return words.join('.').toLowerCase();
        },
        title: function (words) {
            return words.map(capitalise).join(' ');
        },
        sentence: function (words) {
            var joined = words.join(' ').toLowerCase();
            return joined ? capitalise(joined) : '';
        },
        upper: function (words) {
            return words.join(' ').toUpperCase();
        },
        lower: function (words) {
            return words.join(' ').toLowerCase();
        }
    };

    function capitalise(word) {
        return word.charAt(0).toUpperCase() + word.slice(1).toLowerCase();
    }

    function caseUpdate(panel) {
        var text = inputValue(panel, '[data-tool-input]');
        var lines = text.split('\n');

        panel.querySelectorAll('[data-case]').forEach(function (row) {
            var cell = row.querySelector('[data-copy-text]');

            if (!cell) return;

            if (!text.trim()) {
                cell.textContent = '';
                return;
            }

            var convert = CASES[row.getAttribute('data-case')];

            cell.textContent = lines.map(function (line) {
                return convert(splitWords(line));
            }).join('\n');
        });

        setStatus(panel, '');
    }

    /* ----------------------------------------------------------- text stats -- */

    function statsUpdate(panel) {
        var text = inputValue(panel, '[data-tool-input]');
        var trimmed = text.trim();
        var words = trimmed ? trimmed.split(/\s+/).length : 0;

        var values = {
            characters: Array.from(text).length,
            charactersNoSpaces: Array.from(text.replace(/\s/g, '')).length,
            words: words,
            sentences: (text.match(/[^.!?\s][^.!?]*[.!?]+/g) || []).length,
            paragraphs: trimmed ? trimmed.split(/\n\s*\n/).filter(function (block) {
                return block.trim().length > 0;
            }).length : 0,
            lines: text ? text.split('\n').length : 0,
            bytes: toBytes(text).length,
            readingTime: readingTime(words)
        };

        panel.querySelectorAll('[data-stat]').forEach(function (node) {
            var value = values[node.getAttribute('data-stat')];
            node.textContent = typeof value === 'number' ? value.toLocaleString() : value;
        });
    }

    function readingTime(words) {
        var seconds = Math.round((words / 200) * 60);

        if (seconds < 60) return seconds + 's';

        return Math.floor(seconds / 60) + 'm ' + (seconds % 60) + 's';
    }

    /* ------------------------------------------------------------------ diff -- */

    function diffLines(left, right) {
        var rows = left.length;
        var columns = right.length;
        var table = [];
        var i;
        var j;

        for (i = 0; i <= rows; i++) {
            table.push(new Uint32Array(columns + 1));
        }

        for (i = rows - 1; i >= 0; i--) {
            for (j = columns - 1; j >= 0; j--) {
                table[i][j] = left[i] === right[j]
                    ? table[i + 1][j + 1] + 1
                    : Math.max(table[i + 1][j], table[i][j + 1]);
            }
        }

        var result = [];

        i = 0;
        j = 0;

        while (i < rows && j < columns) {
            if (left[i] === right[j]) {
                result.push({ kind: 'same', text: left[i] });
                i++;
                j++;
            } else if (table[i + 1][j] >= table[i][j + 1]) {
                result.push({ kind: 'removed', text: left[i] });
                i++;
            } else {
                result.push({ kind: 'added', text: right[j] });
                j++;
            }
        }

        while (i < rows) {
            result.push({ kind: 'removed', text: left[i] });
            i++;
        }

        while (j < columns) {
            result.push({ kind: 'added', text: right[j] });
            j++;
        }

        return result;
    }

    function diffRun(panel) {
        var result = panel.querySelector('[data-tool-result]');
        var options = readOptions(panel);
        var rawLeft = inputValue(panel, '[data-tool-input]');
        var rawRight = inputValue(panel, '[data-tool-input-2]');

        clearNode(result);

        if (!rawLeft && !rawRight) {
            setStatus(panel, '');
            return;
        }

        var left = rawLeft.split('\n');
        var right = rawRight.split('\n');

        if (left.length * right.length > MAX_DIFF_CELLS) {
            setStatus(panel,
                'Those are too large to compare here: ' + left.length + ' by ' + right.length
                + ' lines. The table this builds grows with the product of the two.');
            return;
        }

        var normalise = function (line) {
            var value = options.trim ? line.trim() : line;
            return options.ignorecase ? value.toLowerCase() : value;
        };

        var changes = diffLines(left.map(normalise), right.map(normalise));
        var added = 0;
        var removed = 0;
        var list = make('div', 'tool-diff');
        var leftIndex = 0;
        var rightIndex = 0;

        changes.forEach(function (change) {
            var original;

            if (change.kind === 'removed') {
                original = left[leftIndex++];
                removed++;
            } else if (change.kind === 'added') {
                original = right[rightIndex++];
                added++;
            } else {
                original = left[leftIndex++];
                rightIndex++;
            }

            var row = make('div', 'tool-diff-line is-' + change.kind);
            var marker = change.kind === 'added' ? '+' : (change.kind === 'removed' ? '-' : ' ');

            row.appendChild(make('span', 'tool-diff-mark', marker));
            row.appendChild(make('span', 'tool-diff-text', original === '' ? ' ' : original));
            list.appendChild(row);
        });

        result.appendChild(list);

        setStatus(panel, added === 0 && removed === 0
            ? 'The two are identical.'
            : plural(added, 'line') + ' added, ' + plural(removed, 'line') + ' removed.');
    }

    /* ----------------------------------------------------------------- regex -- */

    function regexRun(panel) {
        var pattern = inputValue(panel, '[data-regex-pattern]');
        var subject = inputValue(panel, '[data-tool-input]');
        var result = panel.querySelector('[data-tool-result]');
        var flags = '';

        panel.querySelectorAll('[data-regex-flag]').forEach(function (box) {
            if (box.checked) flags += box.getAttribute('data-regex-flag');
        });

        clearNode(result);

        if (!pattern) {
            setStatus(panel, '');
            return;
        }

        var token = nextToken(panel);

        matchInWorker(panel, { pattern: pattern, flags: flags, subject: subject })
            .then(function (data) {
                if (token !== panel.toolToken) return;
                renderMatches(panel, subject, data);
            }, function (error) {
                if (token !== panel.toolToken) return;
                setStatus(panel, error.message);
            });
    }

    /*
     * A pattern that backtracks catastrophically never returns. On this thread that locks the
     * tab up with no way out, so matching happens in a worker which can simply be killed.
     */
    function matchInWorker(panel, request) {
        return new Promise(function (resolve, reject) {
            var source = panel.getAttribute('data-worker-src');

            if (!source || typeof Worker === 'undefined') {
                reject(new Error('This browser cannot run the matcher safely.'));
                return;
            }

            if (panel.toolWorker) panel.toolWorker.terminate();

            var worker = new Worker(source);
            panel.toolWorker = worker;

            var deadline = setTimeout(function () {
                worker.terminate();

                if (panel.toolWorker === worker) panel.toolWorker = null;

                reject(new Error(
                    'That pattern did not finish within a second, so it was stopped. Nested '
                    + 'repetition such as (a+)+ can take exponential time on input that does '
                    + 'not match.'));
            }, REGEX_TIMEOUT_MS);

            worker.onmessage = function (event) {
                clearTimeout(deadline);
                worker.terminate();

                if (panel.toolWorker === worker) panel.toolWorker = null;

                if (event.data && event.data.error) reject(new Error(event.data.error));
                else resolve(event.data);
            };

            worker.onerror = function () {
                clearTimeout(deadline);
                worker.terminate();

                if (panel.toolWorker === worker) panel.toolWorker = null;

                reject(new Error('The matcher could not be started.'));
            };

            worker.postMessage(request);
        });
    }

    function renderMatches(panel, subject, data) {
        var result = panel.querySelector('[data-tool-result]');
        var matches = data.matches || [];

        clearNode(result);

        if (matches.length === 0) {
            setStatus(panel, 'No matches.');
            return;
        }

        setStatus(panel, plural(matches.length, 'match') + (data.truncated ? ', showing the first ' + matches.length + '.' : '.'));

        var highlight = make('div', 'tool-block');
        highlight.appendChild(make('h3', null, 'In context'));

        var preview = make('pre', 'tool-pre tool-highlight');
        var cursor = 0;

        matches.forEach(function (match) {
            if (match.index > cursor) {
                preview.appendChild(document.createTextNode(subject.slice(cursor, match.index)));
            }

            preview.appendChild(make('mark', null, match.text || ' '));
            cursor = match.index + (match.text ? match.text.length : 0);
        });

        if (cursor < subject.length) {
            preview.appendChild(document.createTextNode(subject.slice(cursor)));
        }

        highlight.appendChild(preview);
        result.appendChild(highlight);

        var listing = make('div', 'tool-block');
        listing.appendChild(make('h3', null, 'Matches'));

        var table = make('table', 'tool-table');
        var head = make('thead');
        var headRow = make('tr');

        ['#', 'Position', 'Match', 'Groups'].forEach(function (label) {
            var cell = make('th', null, label);
            cell.setAttribute('scope', 'col');
            headRow.appendChild(cell);
        });

        head.appendChild(headRow);
        table.appendChild(head);

        var body = make('tbody');

        matches.forEach(function (match, index) {
            var row = make('tr');

            row.appendChild(make('td', null, index + 1));
            row.appendChild(make('td', null, match.index));

            var matchCell = make('td');
            matchCell.appendChild(make('code', null, match.text));
            row.appendChild(matchCell);

            row.appendChild(make('td', null, describeGroups(match)));
            body.appendChild(row);
        });

        table.appendChild(body);

        var scroller = make('div', 'tool-table-wrap');
        scroller.appendChild(table);
        listing.appendChild(scroller);
        result.appendChild(listing);
    }

    function describeGroups(match) {
        var parts = [];

        (match.groups || []).forEach(function (value, index) {
            parts.push((index + 1) + ': ' + (value === null ? '(no match)' : value));
        });

        Object.keys(match.named || {}).forEach(function (name) {
            parts.push(name + ': ' + match.named[name]);
        });

        return parts.length ? parts.join('  |  ') : '-';
    }

    /* ------------------------------------------------------------ dispatching -- */

    var CUSTOM = {
        hash: { update: hashUpdate },
        jwt: { update: jwtUpdate },
        'base-convert': { update: baseUpdate },
        timestamp: { update: timestampUpdate, run: timestampNow, init: timestampNow },
        colour: { update: colourUpdate, init: colourInit },
        uuid: { run: uuidGenerate, init: uuidGenerate },
        password: { run: passwordGenerate, init: passwordGenerate },
        'case': { update: caseUpdate },
        'text-stats': { update: statsUpdate },
        diff: { run: diffRun, update: diffRun },
        regex: { update: regexRun }
    };

    function toolName(panel) {
        return panel.getAttribute('data-tool');
    }

    function activeAction(panel) {
        if (panel.dataset.activeAction) return panel.dataset.activeAction;

        var first = panel.querySelector('[data-tool-run]');
        return first ? first.getAttribute('data-tool-run') : null;
    }

    function update(panel, source) {
        var custom = CUSTOM[toolName(panel)];

        if (custom && custom.update) {
            custom.update(panel, source);
            return;
        }

        runTransform(panel, activeAction(panel));
    }

    function run(panel, action) {
        var custom = CUSTOM[toolName(panel)];

        if (custom && custom.run) {
            custom.run(panel, action);
            return;
        }

        runTransform(panel, action);
    }

    function markActive(panel, button) {
        if (!button) return;

        panel.querySelectorAll('[data-tool-run]').forEach(function (other) {
            other.classList.toggle('is-active', other === button);
        });
    }

    var timers = new WeakMap();

    function schedule(panel, source) {
        clearTimeout(timers.get(panel));
        timers.set(panel, setTimeout(function () {
            update(panel, source);
        }, DEBOUNCE_MS));
    }

    /* ------------------------------------------------------------------ copy -- */

    function copyText(text, button) {
        if (!text) return;

        var done = function (ok) {
            var original = button.getAttribute('data-original-label') || button.textContent;

            button.setAttribute('data-original-label', original);
            button.textContent = ok ? 'Copied' : 'Press Ctrl+C';

            setTimeout(function () {
                button.textContent = original;
            }, 1200);
        };

        if (window.xeonCopyText) {
            window.xeonCopyText(text).then(done);
            return;
        }

        done(false);
    }

    /* --------------------------------------------------------------- binding -- */

    document.addEventListener('click', function (event) {
        var button = event.target.closest(
            '[data-tool-run], [data-tool-copy], [data-tool-clear], [data-tool-swap], [data-tool-copy-scope]');

        if (!button) return;

        var panel = button.closest('[data-tool]');
        if (!panel) return;

        event.preventDefault();

        if (button.hasAttribute('data-tool-run')) {
            var action = button.getAttribute('data-tool-run');

            panel.dataset.activeAction = action;
            markActive(panel, button);
            run(panel, action);
            return;
        }

        if (button.hasAttribute('data-tool-copy')) {
            var output = panel.querySelector('[data-tool-output]');
            copyText(output ? output.value : '', button);
            return;
        }

        if (button.hasAttribute('data-tool-copy-scope')) {
            var scope = button.closest('tr') || panel;
            var cell = scope.querySelector('[data-copy-text]');

            copyText(cell ? cell.textContent : '', button);
            return;
        }

        if (button.hasAttribute('data-tool-swap')) {
            var from = panel.querySelector('[data-tool-output]');
            var into = panel.querySelector('[data-tool-input]');

            if (from && into && from.value) {
                into.value = from.value;
                from.value = '';
                update(panel, into);
            }

            return;
        }

        if (button.hasAttribute('data-tool-clear')) {
            panel.querySelectorAll('textarea, input[type="text"], input[type="search"], input[type="datetime-local"]')
                .forEach(function (field) {
                    field.value = '';
                });

            clearNode(panel.querySelector('[data-tool-result]'));

            panel.querySelectorAll('[data-copy-text], [data-ts-out]').forEach(function (cell) {
                cell.textContent = '';
            });

            setStatus(panel, '');
        }
    });

    document.addEventListener('input', function (event) {
        var panel = event.target.closest('[data-tool]');
        if (panel) schedule(panel, event.target);
    });

    document.addEventListener('change', function (event) {
        var panel = event.target.closest('[data-tool]');
        if (panel) schedule(panel, event.target);
    });

    function activate() {
        document.querySelectorAll('[data-tool]').forEach(function (panel) {
            if (panel.dataset.toolReady === 'true') return;

            panel.dataset.toolReady = 'true';
            markActive(panel, panel.querySelector('[data-tool-run]'));

            var custom = CUSTOM[toolName(panel)];

            try {
                if (custom && custom.init) custom.init(panel);
                else update(panel, null);
            } catch (error) {
                setStatus(panel, error.message || 'This tool could not start.');
            }
        });
    }

    document.addEventListener('DOMContentLoaded', activate);
    document.addEventListener('blazor-enhanced-load', activate);

    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', activate);
    }

    activate();
})();
