// Shared behaviour for the docs site: code highlighting, the section rail, and diagram
// step-throughs. Pages define their hops as window.DOC_STEPPERS before this loads.
(function () {
  try {
    if (window.hljs) {
      document.querySelectorAll('pre code:not(.plain)').forEach(function (el) { window.hljs.highlightElement(el); });
    }
  } catch (e) { /* Unhighlighted code still reads fine. */ }

  // Each stepper: { diagram, text, dots, prev, next, all, hops: [{ ids, k, t }] } (element ids).
  (window.DOC_STEPPERS || []).forEach(function (cfg) {
    var diag = document.getElementById(cfg.diagram);
    var text = document.getElementById(cfg.text);
    var dots = document.getElementById(cfg.dots);
    var prev = document.getElementById(cfg.prev);
    var next = document.getElementById(cfg.next);
    var all = document.getElementById(cfg.all);
    if (!diag || !text || !dots || !prev || !next || !all) return;
    var hops = cfg.hops;
    var cur = 0;

    hops.forEach(function (_, i) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = String(i + 1);
      b.setAttribute('aria-label', 'Hop ' + (i + 1));
      b.addEventListener('click', function () { show(i); });
      dots.appendChild(b);
    });

    function clear() {
      diag.querySelectorAll('.on').forEach(function (el) { el.classList.remove('on'); });
    }
    function show(i) {
      cur = i;
      diag.classList.add('stepping');
      clear();
      hops[i].ids.forEach(function (id) {
        var el = document.getElementById(id);
        if (el) el.classList.add('on');
      });
      text.innerHTML = '<span class="k">Hop ' + (i + 1) + ' of ' + hops.length + ' · ' + hops[i].k + '</span>' + hops[i].t;
      Array.prototype.forEach.call(dots.children, function (d, j) { d.classList.toggle('on', j === i); });
      prev.disabled = i === 0;
      next.disabled = i === hops.length - 1;
      all.textContent = 'Show whole map';
    }

    prev.addEventListener('click', function () { if (cur > 0) show(cur - 1); });
    next.addEventListener('click', function () { if (cur < hops.length - 1) show(cur + 1); });
    all.addEventListener('click', function () {
      if (diag.classList.contains('stepping')) {
        diag.classList.remove('stepping');
        clear();
        Array.prototype.forEach.call(dots.children, function (d) { d.classList.remove('on'); });
        text.innerHTML = '<span class="k">Whole map</span>Every path at once. Pick a hop number, or press <b>Next hop</b>, to follow one item through.';
        all.textContent = 'Back to hop ' + (cur + 1);
      } else {
        show(cur);
      }
    });
    show(0);
  });

  // Highlight the rail entry for the section in view.
  var links = Array.prototype.slice.call(document.querySelectorAll('.rail a[href^="#"]'));
  if (links.length && 'IntersectionObserver' in window) {
    var byId = {};
    links.forEach(function (a) { byId[a.getAttribute('href').slice(1)] = a; });
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting && byId[e.target.id]) {
          links.forEach(function (a) { a.classList.remove('active'); });
          byId[e.target.id].classList.add('active');
        }
      });
    }, { rootMargin: '-20% 0px -70% 0px' });
    document.querySelectorAll('section.step').forEach(function (s) { io.observe(s); });
  }
})();
