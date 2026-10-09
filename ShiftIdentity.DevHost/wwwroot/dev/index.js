// The DevHost tools page: the seeded accounts with their current authenticator codes, and the addresses of this run.
(function () {
    'use strict';

    var text = DevHost.text;

    DevHost.accounts(document.getElementById('accounts'), document.getElementById('password'), function (info) {
        var origins = document.getElementById('origins');
        origins.innerHTML = '';
        info.origins.forEach(function (origin) {
            var item = document.createElement('li');
            var link = text('a', origin + '/', 'mono');
            link.href = origin + '/';
            item.appendChild(link);
            if (origin === info.publicOrigin) item.appendChild(text('span', '  (the address the device grant shows to phones)', 'muted'));
            origins.appendChild(item);
        });
    });

    // The Identity app reads its language at startup from the settings it saves in this browser, so a choice here
    // applies to the next page it opens, the device page a phone opens from the code included.
    var languages = { 'en-US': ['English', false], 'ar-IQ': ['Arabic', true], 'ku-IQ': ['Kurdish', true], 'ru-RU': ['Russian', false] };
    var language = document.getElementById('language');

    function settings() {
        try { return JSON.parse(localStorage.getItem('ShiftSettings')) || {}; } catch (e) { return {}; }
    }

    var saved = settings().Language;
    if (saved && languages[saved.CultureName]) language.value = saved.CultureName;
    language.onchange = function () {
        var stored = settings();
        stored.Language = { CultureName: language.value, Label: languages[language.value][0], RTL: languages[language.value][1] };
        try { localStorage.setItem('ShiftSettings', JSON.stringify(stored)); } catch (e) { /* Storage is off: the app stays in its default language. */ }
    };
})();
