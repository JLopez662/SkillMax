// Slide sizing and carousel behaviour for feed posts.
//
// Why this lives in JS: how much text fits on a card can only be known once it's rendered — a
// heading or a list of short items costs more height than its character count suggests. C# splits a
// page into slides on a height *budget*; this picks the actual type size by measuring, then keeps the
// dots and the counter in step with the swipe. Both are pure DOM work, so they stay out of Blazor's
// render loop: at 60fps a scroll handler must not marshal into .NET.
(function () {
    'use strict';

    // Largest first. The pass takes the first size that fits.
    var TIERS = [
        { fs: 21, gap: 15 },
        { fs: 19, gap: 13 },
        { fs: 17.5, gap: 12 },
        { fs: 16, gap: 11 },
        { fs: 14.5, gap: 10 },
        { fs: 13, gap: 9 }      // floor: absorbs a near-miss rather than clipping the last line
    ];

    function applyTier(body, t) {
        body.style.setProperty('--fs', t.fs + 'px');
        body.style.setProperty('--gap', t.gap + 'px');
    }

    // clientHeight includes padding; the content box is what the text actually gets, and the bottom
    // padding is holding the page-source strip.
    function available(body) {
        var cs = getComputedStyle(body);
        return body.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
    }

    function fitSlide(slide) {
        var body = slide.querySelector('.slide-body');
        var flow = slide.querySelector('.flow');
        if (!body || !flow) return;

        var avail = available(body);
        if (avail <= 0) return;

        var chosen = TIERS.length - 1;
        for (var i = 0; i < TIERS.length; i++) {
            applyTier(body, TIERS[i]);
            if (flow.offsetHeight <= avail) { chosen = i; break; }
        }
        applyTier(body, TIERS[chosen]);

        // Room to spare reads as deliberate when it's shared top and bottom.
        var free = avail - flow.offsetHeight;
        body.classList.toggle('center', free > avail * 0.18);
    }

    function fit(root) {
        var slides = (root || document).querySelectorAll('.slide:not(.slide--render)');
        for (var i = 0; i < slides.length; i++) fitSlide(slides[i]);
    }

    // Blazor's OnAfterRender can land before the WebView has laid the card out, and a slide with no
    // height yet measures as zero — which used to leave the default type size in place and drop the
    // last line onto the source strip. Wait for the cards to have a height, then measure.
    function fitWhenLaidOut(root, triesLeft) {
        var slides = root.querySelectorAll('.slide:not(.slide--render)');
        var ready = slides.length === 0;

        for (var i = 0; i < slides.length; i++) {
            var body = slides[i].querySelector('.slide-body');
            if (body && available(body) > 0) { ready = true; break; }
        }

        if (!ready && triesLeft > 0) {
            requestAnimationFrame(function () { fitWhenLaidOut(root, triesLeft - 1); });
            return;
        }
        fit(root);
    }

    // Second line of defence: the first time a card actually gets a width, fit again. Covers a post
    // rendered off-screen, a late font swap, and the window being resized around it.
    function observe(media) {
        if (media.dataset.observed === '1' || typeof ResizeObserver === 'undefined') return;
        media.dataset.observed = '1';

        var last = 0;
        new ResizeObserver(function (entries) {
            var w = entries[0].contentRect.width;
            if (w > 0 && Math.abs(w - last) > 1) { last = w; fit(media); }
        }).observe(media);
    }

    function wire(media) {
        var track = media.querySelector('.slides');
        if (!track) return;
        var post = media.parentNode;

        // Dots and the counter are re-queried on every sync rather than captured: a reshuffle hands
        // this same post a different page, and Blazor patches those nodes underneath us.
        var sync = function () {
            var dots = post.querySelectorAll('.dots i');
            var cur = media.querySelector('.count .cur');
            var total = track.children.length;
            var w = track.clientWidth || 1;

            var i = Math.round(track.scrollLeft / w);
            if (i < 0) i = 0;
            if (i > total - 1) i = total - 1;

            for (var j = 0; j < dots.length; j++) dots[j].classList.toggle('on', j === i);
            if (cur) cur.textContent = i + 1;
        };

        sync();

        if (track.dataset.wired === '1') return;   // don't stack listeners on a reused element
        track.dataset.wired = '1';

        var timer = null;
        track.addEventListener('scroll', function () {
            if (timer) clearTimeout(timer);
            timer = setTimeout(sync, 60);
        }, { passive: true });
    }

    var refitTimer = null;
    function refitAllSoon() {
        if (refitTimer) clearTimeout(refitTimer);
        refitTimer = setTimeout(function () { fit(document); }, 140);
    }

    window.addEventListener('resize', refitAllSoon);
    window.addEventListener('orientationchange', refitAllSoon);

    // ---- reader ----
    // Landing on the arrival page and driving the progress rail are pure DOM concerns; keeping them
    // here means the reader's scroll handler never marshals into .NET.
    window.smReader = {
        open: function (body, anchorId) {
            if (!body) return;

            var target = document.getElementById(anchorId);
            if (target) {
                // Jump, don't animate: the chapter's earlier pages are above, and scrolling through
                // them on entry would look like a mistake.
                var prev = body.style.scrollBehavior;
                body.style.scrollBehavior = 'auto';

                // Measured against the scroll container, not offsetTop — offsetTop is relative to
                // the nearest positioned ancestor (.reader), so it counts the top bar's height and
                // every landing overshoots by it, hiding the seam it was supposed to reveal.
                var delta = target.getBoundingClientRect().top - body.getBoundingClientRect().top;
                body.scrollTop = Math.max(0, body.scrollTop + delta - 14);

                body.style.scrollBehavior = prev;
            }

            var bar = document.getElementById('sm-rprog');
            if (!bar) return;

            var sync = function () {
                var max = body.scrollHeight - body.clientHeight;
                bar.style.width = (max <= 0 ? 100 : (body.scrollTop / max) * 100) + '%';
            };

            if (body.dataset.progWired !== '1') {
                body.dataset.progWired = '1';
                body.addEventListener('scroll', sync, { passive: true });
            }
            sync();
        }
    };

    // ---- shorts ----
    // Some YouTube Shorts refuse to play in an embed even though the channel allows embedding, and
    // nothing YouTube serves ahead of time says which ones. The player is the only oracle, so the
    // card asks it: enablejsapi=1 lets the frame post its state back, and anything that isn't
    // playback inside the deadline is reported to the post, which drops back to its poster.
    //
    // Only one short plays at a time (the feed enforces it), so one watcher is enough.
    var watching = null;
    var HANDSHAKE_MS = 300, DEADLINE_MS = 10000;

    function clearWatch() {
        if (!watching) return;
        clearInterval(watching.poll);
        clearTimeout(watching.deadline);
        watching = null;
    }

    function settle(playing) {
        if (!watching || watching.settled) return;
        watching.settled = true;

        var ref = watching.ref;
        clearWatch();
        if (playing) return;

        // The post owns what a dead frame looks like; this only reports it.
        try { ref.invokeMethodAsync('OnEmbedFailed'); } catch (e) { /* post already gone */ }
    }

    window.addEventListener('message', function (e) {
        if (!watching || watching.settled) return;
        if (e.origin.indexOf('youtube') === -1) return;

        var msg;
        try { msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch (_) { return; }
        if (!msg || !msg.event) return;

        if (msg.event === 'onError') { settle(false); return; }

        // 1 = playing, 3 = buffering. Either one means this video is fine here.
        var state = msg.info && typeof msg.info === 'object' ? msg.info.playerState : msg.info;
        if (state === 1 || state === 3) settle(true);
    });

    window.smShorts = {
        // Called by ShortPost once its player frame is in the DOM.
        watch: function (frame, ref) {
            clearWatch();
            if (!frame) return;

            watching = { ref: ref, settled: false, poll: null, deadline: null };

            // The player only starts talking once it has been asked to; it isn't listening yet
            // when the frame first appears, so keep asking until it answers or we give up.
            var tries = 0;
            watching.poll = setInterval(function () {
                if (!watching || watching.settled || tries++ > 30) return;
                try {
                    frame.contentWindow.postMessage(
                        '{"event":"listening","id":1,"channel":"widget"}', '*');
                } catch (_) { /* frame not ready */ }
            }, HANDSHAKE_MS);

            // Silence is also an answer: a video that won't play here never reaches playing.
            watching.deadline = setTimeout(function () { settle(false); }, DEADLINE_MS);
        },

        // The post stopped playing (another short took over, or the feed was reshuffled).
        stop: clearWatch
    };

    // ---- stories ----
    // The same embed problem as the feed's shorts, one screen bigger. A story is the whole display,
    // so a video that won't play can't just sit there being dead — the viewer is told, and draws
    // another. Two more things a feed post never needed:
    //
    //  * the segment bar follows the real player. A story segment elsewhere is a fixed five
    //    seconds; a short is fifteen to sixty, and the bar is the only thing saying how much is
    //    left. YouTube reports currentTime/duration once we register as listening.
    //  * gestures. Tap left/right, hold to pause, swipe down to close — all measured here and
    //    reported as one verb each, rather than marshalling every pointer event into .NET.
    //
    // Only one story plays at a time, so one watcher is enough.
    var story = null;
    var STORY_DEADLINE_MS = 10000;

    function storyClear() {
        if (!story) return;
        clearInterval(story.poll);
        clearTimeout(story.deadline);
        story = null;
    }

    function storyFill(pct) {
        // The viewer renders finished segments full and gives only the running one this id, so
        // there is exactly one element to write to however many segments the deck has.
        var fill = document.getElementById('sm-story-fill');
        if (fill) fill.style.width = Math.max(0, Math.min(100, pct)) + '%';
    }

    function storyReport(method) {
        if (!story) return;
        var ref = story.ref;
        try { ref.invokeMethodAsync(method); } catch (e) { /* viewer already gone */ }
    }

    function storyFailed() {
        if (!story || story.settled) return;
        story.settled = true;
        storyReport('OnEmbedFailed');
        storyClear();
    }

    function storyEnded() {
        if (!story || story.settled) return;
        story.settled = true;
        storyFill(100);
        storyReport('OnEnded');
        storyClear();
    }

    window.addEventListener('message', function (e) {
        if (!story || story.settled) return;
        if (e.origin.indexOf('youtube') === -1) return;

        var msg;
        try { msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch (_) { return; }
        if (!msg || !msg.event) return;

        if (msg.event === 'onError') { storyFailed(); return; }

        // onStateChange carries the state bare; infoDelivery wraps it with the clock.
        var info = msg.info;
        var state = info && typeof info === 'object' ? info.playerState : info;

        // 1 = playing, 3 = buffering. Either means this video is fine here.
        if (state === 1 || state === 3) story.playing = true;

        // 0 = ended. Only trust it after the video actually started: an embed that was refused
        // can report a stopped player before it ever plays a frame.
        if (state === 0 && story.playing) { storyEnded(); return; }

        if (info && typeof info.currentTime === 'number' && typeof info.duration === 'number'
            && info.duration > 0) {
            storyFill(info.currentTime / info.duration * 100);
        }
    });

    window.smStories = {
        // Called by StoryViewer once its player frame is in the DOM.
        watch: function (frame, ref) {
            storyClear();
            if (!frame) return;

            story = { ref: ref, settled: false, playing: false, poll: null, deadline: null };
            storyFill(0);

            // The player isn't listening yet when the frame first appears, so keep asking until it
            // answers. Once it's playing there's nothing left to ask for.
            var tries = 0;
            story.poll = setInterval(function () {
                if (!story || story.settled || story.playing || tries++ > 40) return;
                try {
                    frame.contentWindow.postMessage(
                        '{"event":"listening","id":1,"channel":"widget"}', '*');
                } catch (_) { /* frame not ready */ }
            }, HANDSHAKE_MS);

            // Silence is also an answer: a video that won't play here never reaches playing.
            story.deadline = setTimeout(function () {
                if (story && !story.playing) storyFailed();
            }, STORY_DEADLINE_MS);
        },

        // pauseVideo / playVideo, for hold-to-pause and for stepping out to YouTube.
        command: function (frame, func) {
            if (!frame) return;
            try {
                frame.contentWindow.postMessage(
                    JSON.stringify({ event: 'command', func: func, args: [] }), '*');
            } catch (_) { /* frame not ready */ }
        },

        // Wired to the transparent layer over the player — the iframe swallows its own pointer
        // events, so the gestures have to live on something above it.
        gestures: function (zones, ref) {
            if (!zones || zones.dataset.wired === '1') return;
            zones.dataset.wired = '1';

            var down = null, hold = null;

            zones.addEventListener('pointerdown', function (e) {
                down = { x: e.clientX, y: e.clientY, held: false };
                hold = setTimeout(function () {
                    if (!down) return;
                    down.held = true;
                    try { ref.invokeMethodAsync('OnHold', true); } catch (_) { }
                }, 220);
            });

            zones.addEventListener('pointerup', function (e) {
                clearTimeout(hold);
                if (!down) return;

                var dx = e.clientX - down.x, dy = e.clientY - down.y, held = down.held;
                down = null;

                try {
                    if (held) { ref.invokeMethodAsync('OnHold', false); return; }
                    if (dy > 90 && Math.abs(dx) < 70) { ref.invokeMethodAsync('OnSwipeClose'); return; }

                    // A finger that travelled was going somewhere else, not tapping.
                    if (Math.abs(dx) > 12 || Math.abs(dy) > 12) return;

                    var box = zones.getBoundingClientRect();
                    ref.invokeMethodAsync('OnTap', (e.clientX - box.left) / box.width < 0.32);
                } catch (_) { /* viewer already gone */ }
            });

            zones.addEventListener('pointercancel', function () {
                clearTimeout(hold);
                if (down && down.held) {
                    try { ref.invokeMethodAsync('OnHold', false); } catch (_) { }
                }
                down = null;
            });
        },

        stop: storyClear
    };

    window.smCards = {
        // Called by FeedPost once its slides are in the DOM — on first load and again whenever the
        // post is handed a different page.
        init: function (media) {
            if (!media) return;

            // The element survives a reshuffle, so a carousel left on slide 3 would open the new
            // page mid-way through. Back to the first card.
            var track = media.querySelector('.slides');
            if (track) track.scrollLeft = 0;

            observe(media);
            wire(media);
            fitWhenLaidOut(media, 20);
        },
        // How wide the card really is, in CSS pixels. The text budget is a function of this: a 4:5
        // card's area — and so the amount of text it holds — grows with the square of its width.
        cardWidth: function (media) {
            if (media && media.clientWidth > 0) return media.clientWidth;
            return Math.min(window.innerWidth || 393, 480);   // matches the shell's max-width
        },

        refit: refitAllSoon
    };
})();
