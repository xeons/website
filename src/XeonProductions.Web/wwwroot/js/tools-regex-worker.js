/*
 * Runs one regular expression against one subject, off the main thread.
 *
 * The isolation is the whole point. A pattern with nested quantifiers can take exponential
 * time on input that does not match, and there is no way to interrupt it once it starts. Here
 * the page can terminate the worker and carry on; on the main thread the tab would be lost.
 */
'use strict';

var MAX_MATCHES = 500;

self.onmessage = function (event) {
    var request = event.data || {};
    var expression;

    try {
        expression = new RegExp(request.pattern, request.flags || '');
    } catch (error) {
        self.postMessage({ error: error.message });
        return;
    }

    var subject = request.subject || '';
    var matches = [];
    var truncated = false;

    try {
        if (expression.global) {
            var found;

            while ((found = expression.exec(subject)) !== null) {
                matches.push(describe(found));

                if (matches.length >= MAX_MATCHES) {
                    truncated = true;
                    break;
                }

                /* A pattern able to match nothing never advances lastIndex on its own. */
                if (found[0] === '') expression.lastIndex++;
            }
        } else {
            var single = expression.exec(subject);
            if (single) matches.push(describe(single));
        }
    } catch (error) {
        self.postMessage({ error: error.message });
        return;
    }

    self.postMessage({ matches: matches, truncated: truncated });
};

function describe(found) {
    return {
        index: found.index,
        text: found[0],
        groups: Array.prototype.slice.call(found, 1).map(function (value) {
            return value === undefined ? null : value;
        }),
        named: found.groups ? Object.assign({}, found.groups) : {}
    };
}
