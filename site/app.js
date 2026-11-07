(() => {
  const root = document.getElementById('demo');
  if (!root) return;

  const songs = [
    { title: 'Bohemian Rhapsody', artist: 'Queen' },
    { title: 'Take On Me', artist: 'a-ha' },
    { title: 'Hotel California', artist: 'Eagles' },
    { title: 'Dancing Queen', artist: 'ABBA' },
    { title: 'Sweet Caroline', artist: 'Neil Diamond' },
    { title: 'Mr. Brightside', artist: 'The Killers' }
  ];

  const labels = {
    queue: root.dataset.queueLabel || 'Queue',
    queued: root.dataset.queuedLabel || 'Queued',
    empty: root.dataset.emptyLabel || 'Queue is empty',
    play: root.dataset.playLabel || 'Play',
    pause: root.dataset.pauseLabel || 'Pause',
    playing: root.dataset.playingLabel || 'Playing',
    paused: root.dataset.pausedLabel || 'Paused',
    nothing: root.dataset.nothingLabel || 'Nothing playing'
  };
  const nothingFound = document.documentElement.lang === 'ru'
    ? 'Ничего не найдено'
    : 'Nothing found';

  const input = document.getElementById('demoSearch');
  const results = document.getElementById('demoResults');
  const queueList = document.getElementById('demoQueue');
  const queueEmpty = document.getElementById('demoQueueEmpty');
  const queueCount = document.getElementById('demoQueueCount');
  const nowTitle = document.getElementById('demoNowTitle');
  const nowState = document.getElementById('demoNowState');
  const playButton = document.getElementById('demoPlay');
  const eq = document.getElementById('demoEq');

  const queue = [];
  let current = null;
  let playing = false;

  const queued = song => queue.some(item => item.title === song.title && item.artist === song.artist);
  const escape = value => String(value).replace(/[&<>"']/g, ch => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[ch]);

  function renderResults() {
    const query = (input.value || '').trim().toLowerCase();
    const matches = songs.filter(song =>
      !query || `${song.title} ${song.artist}`.toLowerCase().includes(query));

    results.innerHTML = matches.length
      ? matches.map(song => `
          <div class="demo-song">
            <div><strong>${escape(song.title)}</strong><span>${escape(song.artist)}</span></div>
            <button type="button" data-title="${escape(song.title)}" data-artist="${escape(song.artist)}"
              class="${queued(song) ? 'queued' : ''}" aria-pressed="${queued(song)}">
              ${queued(song) ? escape(labels.queued) : escape(labels.queue)}
            </button>
          </div>`).join('')
      : `<div class="demo-empty">${escape(nothingFound)}</div>`;
  }

  function renderQueue() {
    queueCount.textContent = queue.length;
    queueEmpty.style.display = queue.length ? 'none' : 'block';
    queueList.innerHTML = queue.map((song, index) => `
      <li><b>${index + 1}</b><span>${escape(song.title)} <em>${escape(song.artist)}</em></span></li>`
    ).join('');
  }

  function renderNow() {
    nowTitle.textContent = current ? `${current.title} — ${current.artist}` : labels.nothing;
    nowState.textContent = playing ? labels.playing : labels.paused;
    playButton.textContent = playing ? labels.pause : labels.play;
    eq.classList.toggle('on', playing && !!current);
    playButton.setAttribute('aria-pressed', String(playing));
  }

  function render() {
    renderResults();
    renderQueue();
    renderNow();
  }

  function addToQueue(title, artist) {
    if (queued({ title, artist })) return;
    const song = { title, artist };
    queue.push(song);
    if (!current) current = song;
    render();
  }

  results.addEventListener('click', event => {
    const button = event.target.closest('button[data-title]');
    if (!button) return;
    addToQueue(button.dataset.title, button.dataset.artist);
  });

  input.addEventListener('input', renderResults);
  input.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
      input.value = '';
      renderResults();
    }
  });

  playButton.addEventListener('click', () => {
    if (!current) {
      current = queue[0] || songs[0];
      if (!queue.length) {
        queue.push(current);
        renderQueue();
      }
    }
    playing = !playing;
    renderNow();
  });

  render();
})();
