window.fullCalendarInterop = (function () {
    var _instances = {};

    function loadResource(tag, attrs) {
        return new Promise(function (resolve, reject) {
            var existing = document.querySelector(tag + '[' + Object.keys(attrs).map(function (k) { return k + '="' + attrs[k] + '"'; }).join('][') + ']');
            if (existing) { resolve(); return; }
            var el = document.createElement(tag);
            for (var k in attrs) el.setAttribute(k, attrs[k]);
            el.onload = resolve;
            el.onerror = function() { reject(new Error('Failed to load ' + (attrs.src || attrs.href))); };
            document.head.appendChild(el);
        });
    }

    function ensureFullCalendar() {
        if (window.FullCalendar) return Promise.resolve();
        return loadResource('script', {
            src: 'https://cdn.jsdelivr.net/npm/fullcalendar@6.1.17/index.global.min.js'
        });
    }

    return {
        _instances: _instances,

        init: function (elementId, dotNetRef) {
            return ensureFullCalendar().then(function () {
                var calendarEl = document.getElementById(elementId);
                if (!calendarEl) return;

                var calendar = new FullCalendar.Calendar(calendarEl, {
                    initialView: 'dayGridMonth',
                    height: '100%',
                    selectable: true,
                    selectMirror: true,
                    headerToolbar: {
                        left: '',
                        center: 'title',
                        right: 'today prev,next'
                    },
                    eventOrder: 'start',
                    datesSet: function (dateInfo) {
                        dotNetRef.invokeMethodAsync('OnDatesChanged',
                            dateInfo.start.toISOString(),
                            dateInfo.end.toISOString());
                    },
                    select: function (selectInfo) {
                        calendar.unselect();
                        dotNetRef.invokeMethodAsync('OnDateRangeSelected',
                            selectInfo.startStr, selectInfo.endStr);
                    },
                    eventClick: function (info) {
                        dotNetRef.invokeMethodAsync('OnEventClicked',
                            parseInt(info.event.id));
                    },
                    eventDidMount: function (info) {
                        var title = info.event.extendedProps
                            ? (info.event.extendedProps.calendarTitle || '')
                            : '';
                        if (title) {
                            info.el.setAttribute('title', title);
                        }
                    }
                });

                calendar.render();
                _instances[elementId] = calendar;
            });
        },

        setEvents: function (elementId, events) {
            var calendar = _instances[elementId];
            if (!calendar) return;

            var sources = calendar.getEventSources();
            for (var i = 0; i < sources.length; i++) {
                sources[i].remove();
            }

            calendar.addEventSource(events);
        },

        destroy: function (elementId) {
            var calendar = _instances[elementId];
            if (calendar) {
                calendar.destroy();
                delete _instances[elementId];
            }
        }
    };
})();
