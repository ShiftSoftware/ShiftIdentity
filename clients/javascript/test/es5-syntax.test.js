'use strict';
// The shipped files must run in old browsers without a build step, so they must stay plain ES5.
// This is a text scan, not a parser. It removes comments and string literals first. The sources do not use
// regular expression literals, so the scan also checks that none appear.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const SOURCE = path.join(__dirname, '..', 'src');
const FILES = fs.readdirSync(SOURCE).filter(file => file.endsWith('.js'));

function codeOnly(source) {
    let code = '';
    let i = 0;
    while (i < source.length) {
        const c = source[i], next = source[i + 1];
        if (c === '/' && next === '/') {
            while (i < source.length && source[i] !== '\n') i++;
        } else if (c === '/' && next === '*') {
            const end = source.indexOf('*/', i + 2);
            assert.notEqual(end, -1, 'A block comment is not closed.');
            i = end + 2;
            code += ' ';
        } else if (c === '"' || c === "'") {
            i++;
            while (i < source.length && source[i] !== c) {
                assert.notEqual(source[i], '\n', 'A string literal crosses a line.');
                i += source[i] === '\\' ? 2 : 1;
            }
            i++;
            code += '""';
        } else {
            code += c;
            i++;
        }
    }
    return code;
}

const FORBIDDEN = [
    ['an arrow function', /=>/],
    ['a template literal', /`/],
    ['let', /\blet\b/],
    ['const', /\bconst\b/],
    ['a class', /\bclass\b/],
    ['async or await', /\b(async|await)\b/],
    ['a generator', /\bfunction\s*\*|\byield\b/],
    ['a module statement', /\b(import|export)\b/],
    ['spread or rest', /\.\.\./],
    ['optional chaining or nullish coalescing', /\?\.|\?\?/],
    ['the exponent operator', /\*\*/],
    ['for...of', /\bfor\s*\([^)]*\bof\b/],
    ['a default parameter', /\bfunction\b[^(]*\([^)]*=/],
    // A name, a plain parameter list and a body, after "{" or ",". A call such as later(function () {...}) does not match.
    ['a shorthand method', /[{,]\s*(?!if\b|for\b|while\b|switch\b|catch\b|function\b|return\b)\w+\s*\(\s*(\w+\s*(,\s*\w+\s*)*)?\)\s*\{/],
    ['a computed property name', /[{,]\s*\[[^\]]*\]\s*:/],
    ['a newer built-in', /\b(Symbol|Map|Set|WeakMap|WeakSet|Proxy|Reflect)\b|Object\.(assign|entries|values)\b|Array\.(from|of)\b|Number\.(isFinite|isNaN|isInteger)\b/],
    ['a newer method', /\.(includes|startsWith|endsWith|repeat|padStart|padEnd|find|findIndex|fill)\s*\(/],
    ['a regular expression literal', /[=(,:!&|?{};]\s*\/[^/*]/],
    ['a trailing comma (rejected by old engines)', /,\s*[}\]]/]
];

for (const file of FILES) {
    test(file + ' uses only ES5 syntax and built-ins', () => {
        const code = codeOnly(fs.readFileSync(path.join(SOURCE, file), 'utf8'));
        for (const [name, pattern] of FORBIDDEN) {
            const match = code.match(pattern);
            assert.equal(match, null, file + ' uses ' + name + ': ' + (match && code.substr(Math.max(0, match.index - 40), 100)));
        }
    });
}

test('the scan finds the constructs it looks for', () => {
    const samples = {
        'an arrow function': 'var f = x => x;',
        'a template literal': 'var s = `x`;',
        'let': 'let x = 1;',
        'a shorthand method': 'var o = { read(callback) { return 1; } };',
        'a default parameter': 'function f(a, b = 2) {}',
        'a newer method': 'list.includes(1);',
        'a regular expression literal': 'var r = /x+/;',
        'a trailing comma (rejected by old engines)': 'var o = { a: 1, };'
    };
    for (const [name, sample] of Object.entries(samples)) {
        const pattern = FORBIDDEN.find(entry => entry[0] === name)[1];
        assert.match(codeOnly(sample), pattern, name);
    }
    // Words inside strings and comments are not code.
    assert.doesNotMatch(codeOnly('var s = "let => `"; // const x = 1'), /let|=>|`|const/);
    assert.ok(FILES.length >= 2);
});
