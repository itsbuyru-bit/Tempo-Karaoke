namespace TempoKaraoke.Services;

/// <summary>
/// Мобильная страница пульта. Стилистика — тёмный мобильный пульт,
/// данные и действия приходят из /api/* этого же процесса.
/// </summary>
public static class RemotePage
{
    public const string Html = """
<!doctype html>
<html lang="en-US">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
<meta name="theme-color" content="#0d0f12">
<link rel="icon" type="image/png" href="/appicon.png">
<title>Tempo Karaoke — remote</title>
<style>
:root{--bg:#12151B;--surface:#171B24;--card:#1B202B;--border:#2A3140;--primary:#00A3FF;--primary-dark:#0077CC;--green:#22c55e;--red:#ef4444;--amber:#f59e0b;--text:#f1f5f9;--muted:#94a3b8;--sub:#64748b}
*{box-sizing:border-box;margin:0;padding:0;-webkit-tap-highlight-color:transparent;user-select:none}
body{background:var(--bg);color:var(--text);font:16px/1.4 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Arial,sans-serif;min-height:100vh;display:flex;justify-content:center;padding:12px 12px calc(24px + env(safe-area-inset-bottom));overflow-x:hidden}
.app{width:100%;max-width:520px;display:flex;flex-direction:column;gap:14px;padding-bottom:24px}
header{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:4px 2px 10px;border-bottom:1px solid var(--border)}
.brand{display:flex;align-items:center;gap:8px;font-weight:800;font-size:1.15rem;min-width:0}
.brand span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0}
.brand-icon{width:34px;height:34px;border-radius:9px;object-fit:contain;display:block;flex:0 0 auto}
.header-tools{display:flex;align-items:center;gap:6px;flex:0 0 auto}
.lang-select{height:40px;width:66px;padding:0 6px;border:1px solid var(--border);border-radius:10px;background:var(--card);color:var(--text);font:inherit;font-size:.74rem;font-weight:800;letter-spacing:.5px;text-align:center;cursor:pointer;outline:none}
.role{display:flex;align-items:center;gap:6px;min-height:40px;padding:6px 12px;border-radius:999px;font-size:.8rem;font-weight:600;background:var(--card);border:1px solid var(--border);color:var(--muted);cursor:pointer}
.role.host{display:flex;flex-direction:row;align-items:center;background:rgba(245,158,11,.14);border-color:rgba(245,158,11,.5);color:#fbbf24}
.role-text{max-width:138px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.role-pin{margin-left:2px;padding:2px 0 2px 9px;border:0;border-left:1px solid var(--border);background:transparent;font:inherit;font-size:.68rem;font-weight:800;letter-spacing:.55px;color:var(--sub);cursor:pointer}
.role-pin:active{color:var(--primary)}
.role.host .role-pin{display:none}
.role-dot{width:8px;height:8px;border-radius:50%;background:var(--sub)}
.role.host .role-dot{background:#fbbf24;box-shadow:0 0 8px #fbbf24}
.role-wrap{position:relative;display:flex;justify-content:flex-end}
.user-profile-dropdown{position:relative;display:flex;align-items:center}
.host-badge-btn{display:flex;align-items:center;gap:6px;min-height:40px;padding:6px 11px;border-radius:8px;font:inherit;font-size:.8rem;font-weight:700;background:var(--card);border:1px solid var(--border);color:var(--muted);cursor:pointer;white-space:nowrap}
.host-badge-btn.is-host{background:rgba(0,163,255,.14);border-color:rgba(0,163,255,.55);color:#7dd3fc}
.host-dot{width:8px;height:8px;border-radius:50%;background:var(--sub);flex:0 0 auto}
.host-badge-btn.is-host .host-dot{background:var(--primary);box-shadow:0 0 8px var(--primary)}
.profile-menu{position:absolute;right:0;top:calc(100% + 8px);display:none;min-width:232px;background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:6px;box-shadow:0 16px 36px rgba(0,0,0,.55);z-index:80}
.profile-menu.open{display:block}
.profile-menu-header{padding:8px 12px 10px;color:var(--muted);font-size:.76rem;border-bottom:1px solid var(--border);margin-bottom:5px}
.profile-menu-header strong{color:var(--text)}
.profile-menu-item{width:100%;min-height:42px;border:0;border-radius:8px;background:transparent;color:var(--text);font:inherit;font-size:.84rem;text-align:left;padding:0 12px;cursor:pointer}
.profile-menu-item:hover,.profile-menu-item:focus-visible{background:rgba(255,255,255,.06);outline:none}
.profile-menu-item.text-danger{color:#fca5a5}
.profile-menu-item.text-danger:hover{background:rgba(239,68,68,.12)}
.icon-btn{position:relative;width:40px;height:40px;min-width:40px;padding:0;border:1px solid var(--border);border-radius:8px;background:var(--card);color:var(--text);display:inline-flex;align-items:center;justify-content:center;cursor:pointer}
.icon-btn:active{transform:scale(.95);background:var(--border)}
.icon-bell{width:20px;height:20px;stroke:currentColor;fill:none;stroke-width:1.9;stroke-linecap:round;stroke-linejoin:round}
.notification-badge{position:absolute;top:-5px;right:-5px;min-width:18px;height:18px;padding:0 4px;border-radius:9px;background:var(--primary);color:#fff;font:800 .64rem/18px inherit;text-align:center;box-shadow:0 0 0 2px var(--bg);animation:notifyPulse 1.8s ease-in-out infinite}
.notification-badge[hidden]{display:none}
@keyframes notifyPulse{0%,100%{box-shadow:0 0 0 2px var(--bg),0 0 0 0 rgba(0,163,255,.55)}50%{box-shadow:0 0 0 2px var(--bg),0 0 0 7px rgba(0,163,255,0)}}
@media (max-width:430px){.brand{font-size:1rem;gap:6px}.brand span{display:block;white-space:normal;line-height:1.05;max-width:118px}.host-badge-btn{padding:6px 8px}.role-text{max-width:140px}.lang-select{padding:0 5px}.header-tools{gap:4px}}
.player{background:linear-gradient(180deg,var(--surface),var(--card));border:1px solid var(--border);border-radius:12px;padding:16px;display:flex;flex-direction:column;gap:12px;box-shadow:0 8px 24px rgba(0,0,0,.35)}
.player-top{display:flex;justify-content:space-between;align-items:center}
.status{font-size:.72rem;text-transform:uppercase;font-weight:700;letter-spacing:.8px;color:var(--primary);display:flex;align-items:center;gap:6px}
.dot{width:7px;height:7px;border-radius:50%;background:var(--primary);box-shadow:0 0 8px var(--primary)}
.track{display:flex;flex-direction:column;gap:3px;min-width:0}
.track-title{font-size:1.25rem;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.track-sub{font-size:.9rem;color:var(--muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.progress{display:flex;flex-direction:column;gap:6px}
.timeline{width:100%;height:30px;margin:0;background:transparent;accent-color:var(--primary);cursor:pointer;-webkit-appearance:none;appearance:none}
.timeline::-webkit-slider-runnable-track{height:6px;border-radius:99px;background:linear-gradient(90deg,var(--primary) 0%,var(--primary) var(--seek,0%),#242a36 var(--seek,0%),#242a36 100%)}
.timeline::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:14px;height:14px;margin-top:-4px;border-radius:50%;background:#fff;border:3px solid var(--primary);box-shadow:0 0 8px rgba(56,189,248,.45)}
.timeline::-moz-range-track{height:6px;border-radius:99px;background:#242a36}
.timeline::-moz-range-progress{height:6px;border-radius:99px;background:var(--primary)}
.timeline::-moz-range-thumb{width:14px;height:14px;border-radius:50%;background:#fff;border:3px solid var(--primary)}
.progress-bg{height:7px;background:#242a36;border-radius:99px;overflow:hidden;cursor:pointer}
.progress-fill{height:100%;width:0;background:linear-gradient(90deg,var(--primary-dark),var(--primary));border-radius:99px;transition:width .25s linear;box-shadow:0 0 10px rgba(56,189,248,.25)}
.times{display:flex;justify-content:space-between;font:.72rem monospace;color:var(--sub);font-family:monospace;font-weight:600}
.host{display:none;flex-direction:column;gap:10px;padding-top:10px;border-top:1px solid var(--border)}
.host.visible{display:flex}
.buttons{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}
.ctrl{background:var(--surface);border:1px solid var(--border);color:var(--text);min-height:54px;padding:10px 0;border-radius:12px;font:inherit;font-size:.82rem;font-weight:700;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:4px;cursor:pointer}
.ctrl:active{transform:scale(.96);background:var(--card)}
.ctrl.play{background:var(--primary);color:#071018;border-color:var(--primary);box-shadow:0 0 14px rgba(56,189,248,.25)}
.volume{display:flex;align-items:center;gap:10px;min-height:44px;background:rgba(0,0,0,.2);padding:8px 12px;border-radius:12px;font-size:.8rem;color:var(--muted)}
.volume input{flex:1;min-width:60px;height:28px}
.volume b{color:var(--primary);font-family:monospace;min-width:26px;text-align:right}
.guest-note{font-size:.75rem;color:var(--sub);text-align:center;padding-top:4px;border-top:1px solid var(--border)}
.guest-note a{color:var(--primary);text-decoration:underline;cursor:pointer}
.tabs{display:flex;background:var(--surface);padding:4px;border-radius:12px;border:1px solid var(--border);gap:4px;position:sticky;top:0;z-index:20}
.tab{flex:1;min-height:44px;background:transparent;border:0;color:var(--muted);padding:10px 0;font:inherit;font-size:.9rem;font-weight:700;border-radius:8px;cursor:pointer;display:flex;align-items:center;justify-content:center;gap:6px}
.tab.active{background:var(--card);color:var(--text);box-shadow:0 2px 8px rgba(0,0,0,.3)}
.count{font-size:.72rem;background:#2a313f;color:var(--primary);padding:2px 7px;border-radius:99px}
.host-queue-tools{display:none;gap:8px}
.host-queue-tools.visible{display:flex}
.host-queue-tools button{flex:1;min-height:40px;border:1px solid var(--border);border-radius:8px;background:var(--card);color:var(--text);font:inherit;font-size:.78rem;font-weight:700;cursor:pointer}
.host-queue-tools button:first-child{color:#fca5a5}
.host-queue-tools button:active{background:var(--border)}
.search-wrap{position:sticky;top:58px;z-index:19;display:none;width:100%;padding:4px 0;background:var(--bg)}
.search-wrap.visible{display:flex}
.search{flex:1;min-width:0;width:100%;height:42px;padding:0 40px 0 40px;background:var(--surface);border:1px solid var(--border);border-radius:10px;color:var(--text);font:inherit;font-size:16px;outline:none}
.search:focus{border-color:var(--primary);box-shadow:0 0 0 2px rgba(0,163,255,.16)}
.search-icon{position:absolute;left:13px;top:50%;transform:translateY(-50%);color:var(--sub);pointer-events:none;font-size:1rem}
.clear{position:absolute;right:5px;top:50%;transform:translateY(-50%);width:32px;height:32px;border:0;border-radius:8px;background:transparent;color:var(--muted);display:none;font-size:.9rem}
.clear.visible{display:grid;place-items:center}
.clear:active{background:var(--border)}
.search-suggest{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:30;display:flex;flex-direction:column;overflow:hidden;background:var(--surface);border:1px solid var(--border);border-radius:10px;box-shadow:0 12px 32px rgba(0,0,0,.35)}
.search-suggest[hidden]{display:none}
.search-suggest button{display:block;width:100%;padding:10px 12px;border:0;border-bottom:1px solid var(--border);background:transparent;color:var(--text);font:inherit;font-size:.9rem;text-align:left;cursor:pointer}
.search-suggest button:last-child{border-bottom:0}
.search-suggest button:active{background:var(--border)}
input[type="search"]::-webkit-search-cancel-button,
input[type="search"]::-webkit-search-decoration{-webkit-appearance:none;appearance:none;display:none}
input,textarea{user-select:text;font-size:16px}
.list{display:flex;flex-direction:column;gap:8px;min-height:280px}
.item{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:12px;display:flex;align-items:center;gap:10px}
.item.drag{opacity:.35;border-color:var(--primary)}
.item.drag-over{border-top:3px solid var(--primary)}
.item.playing{background:linear-gradient(135deg,rgba(56,189,248,.16),rgba(34,197,94,.1));border-color:rgba(56,189,248,.5);box-shadow:0 0 0 1px rgba(56,189,248,.18),0 0 18px rgba(56,189,248,.12)}
.item.playing .name{color:var(--primary);font-weight:800}
.item.mine{border-color:rgba(56,189,248,.55);box-shadow:inset 0 0 0 1px rgba(56,189,248,.18)}
.item.past{opacity:.45}
.item.past:hover{opacity:1}
.queue-divider{display:flex;align-items:center;gap:8px;color:var(--muted);font-size:.69rem;font-weight:800;letter-spacing:1px;padding:2px 4px;margin:2px 0}
.queue-divider:before,.queue-divider:after{content:"";height:1px;flex:1;background:var(--border)}
.now-badge{display:inline-flex;align-items:center;gap:4px;background:var(--primary);color:#071018;border-radius:5px;padding:2px 6px;font-size:.62rem;font-weight:900;letter-spacing:.45px;white-space:nowrap}
.eq{display:flex;align-items:flex-end;gap:2px;width:20px;height:18px;flex:0 0 auto}
.eq span{width:3px;border-radius:2px;background:var(--primary);animation:eq .8s ease-in-out infinite}
.eq span:nth-child(1){height:60%;animation-delay:-.2s}.eq span:nth-child(2){height:100%}.eq span:nth-child(3){height:72%;animation-delay:-.45s}
@keyframes eq{0%,100%{transform:scaleY(.45)}50%{transform:scaleY(1)}}
.queue-divider{display:flex;align-items:center;gap:8px;color:var(--sub);font-size:.65rem;font-weight:800;letter-spacing:.9px;padding:2px 4px;margin:2px 0}
.queue-divider:before,.queue-divider:after{content:"";height:1px;flex:1;background:var(--border)}
.add.added{background:#15803D;border-color:#15803D;color:#fff}
.count.bump{animation:bump .35s ease}
@keyframes bump{0%,100%{transform:scale(1)}45%{transform:scale(1.28);filter:brightness(1.35)}}
.badge{font-size:.68rem;color:var(--sub);background:#232834;border:1px solid var(--border);padding:1px 6px;border-radius:6px;flex:0 0 auto}
.now{font-size:.68rem;color:var(--primary);font-weight:700;flex:0 0 auto}
.now-banner{padding:10px 12px;border-radius:12px;background:rgba(56,189,248,.12);border:1px solid rgba(56,189,248,.35);color:var(--primary);font-size:.84rem;font-weight:600;line-height:1.3}
.delete.info{color:var(--primary)}
.toast{white-space:normal;max-width:90vw;text-align:center;bottom:calc(24px + env(safe-area-inset-bottom))}
.num{font:700 .82rem monospace;color:var(--sub);width:20px;text-align:center;flex:0 0 auto}
.num-play{width:34px;height:34px;padding:0;border:1px solid rgba(56,189,248,.45);border-radius:50%;background:rgba(56,189,248,.08);color:var(--primary);display:inline-flex;align-items:center;justify-content:center;cursor:pointer}
.num-play .num-glyph{display:none;font-size:.7rem}
.num-play:hover .num-value,.num-play:active .num-value{display:none}
.num-play:hover .num-glyph,.num-play:active .num-glyph{display:inline}
.num.quick{border:0;background:transparent;cursor:pointer;color:var(--primary);padding:0}
.num.quick:active{transform:scale(.9)}
.info{flex:1;min-width:0;display:flex;flex-direction:column;gap:2px}
.name{font-size:.92rem;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.sub{display:flex;align-items:center;gap:8px;font-size:.78rem;color:var(--muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.add{min-height:40px;padding:9px 12px;border:1px solid var(--border);border-radius:10px;background:var(--card);color:var(--primary);font:inherit;font-size:.8rem;font-weight:700;flex:0 0 auto}
.add:active{background:var(--primary);color:#071018}
.queue-actions{display:flex;align-items:center;justify-content:flex-end;gap:16px;height:100%}
.queue-actions .handle,.queue-actions .delete{align-self:center}
.delete.info{margin:0;padding:0;line-height:1;display:inline-flex;align-items:center;justify-content:center;color:var(--primary)}
.quick-play{width:30px;height:30px;min-width:30px;padding:0;border:1px solid rgba(56,189,248,.45);border-radius:50%;background:rgba(56,189,248,.1);color:var(--primary);font-size:.72rem;display:inline-flex;align-items:center;justify-content:center;flex:0 0 auto;cursor:pointer}
.quick-play:active{background:var(--primary);color:#071018}
.catalog-actions{display:flex;align-items:center;gap:4px;flex:0 0 auto}
.item.catalog-card{align-items:center;gap:8px;padding:10px 11px}
.catalog-card .info{flex:1;min-width:0;display:flex;flex-direction:column;gap:3px}
.catalog-card .name{font-weight:700;white-space:normal;overflow-wrap:anywhere;word-break:break-word;line-height:1.25}
.catalog-card .sub{display:flex;align-items:center;gap:6px;min-width:0;white-space:nowrap;overflow:hidden}
.catalog-card .sub .artist{min-width:0;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.catalog-card .sub .duration{flex:0 0 auto}
.catalog-play,.catalog-add,.catalog-next{width:34px;height:34px;min-width:34px;padding:0;border-radius:8px;border:1px solid rgba(0,163,255,.45);background:rgba(0,163,255,.12);color:var(--primary);font:inherit;font-size:1rem;font-weight:800;display:inline-flex;align-items:center;justify-content:center;cursor:pointer;flex:0 0 auto}
.catalog-play:active,.catalog-add:active,.catalog-next:active,.catalog-add.added{background:var(--primary);color:#06121c}
.catalog-next.btn-success{background:rgba(34,197,94,.15);border-color:rgba(34,197,94,.55);color:#4ade80;cursor:default}
@media (max-width:390px){.catalog-card{padding:9px;gap:6px}.catalog-card .name{font-size:.88rem}.catalog-play,.catalog-add,.catalog-next{width:32px;height:32px;min-width:32px;font-size:.92rem}}
.add.play-now{color:var(--text);border-color:rgba(56,189,248,.5);background:rgba(56,189,248,.12)}
.order{display:flex;flex-direction:column;gap:2px}
.arrow{width:24px;height:18px;border:1px solid var(--border);border-radius:5px;background:var(--card);color:var(--muted);font-size:.65rem;display:grid;place-items:center}
.arrow:disabled{opacity:.2}
.handle{width:38px;height:44px;padding:8px;display:grid;place-items:center;color:var(--sub);cursor:grab;touch-action:none;font-size:1.05rem;box-sizing:border-box}
.delete{width:42px;height:44px;padding:8px;border:0;background:transparent;color:var(--red);font-size:1rem;border-radius:8px;box-sizing:border-box}
.delete:active{background:rgba(239,68,68,.12)}
.handle{width:38px;height:44px;padding:8px}
.empty{text-align:center;padding:40px 16px;color:var(--sub);display:flex;flex-direction:column;gap:8px;font-size:.9rem}
.modal{position:fixed;inset:0;background:rgba(0,0,0,.75);backdrop-filter:blur(6px);display:none;align-items:center;justify-content:center;padding:16px;z-index:100}
.modal.active{display:flex}
.pin-box{background:var(--surface);border:1px solid var(--border);border-radius:24px;width:100%;max-width:330px;padding:24px;display:flex;flex-direction:column;align-items:center;gap:16px;box-shadow:0 16px 40px rgba(0,0,0,.6)}
.pin-title{font-weight:700;font-size:1.15rem}
.pin-sub{font-size:.78rem;color:var(--muted);text-align:center}
.dots{display:flex;gap:14px;margin:6px 0}
.pin-dot{width:14px;height:14px;border-radius:50%;background:var(--card);border:1.5px solid var(--border)}
.pin-dot.full{background:var(--primary);border-color:var(--primary);box-shadow:0 0 10px rgba(56,189,248,.25)}
.pad{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;width:100%}
.key{height:52px;border:1px solid var(--border);border-radius:14px;background:var(--card);color:var(--text);font:600 1.35rem inherit;cursor:pointer}
.key:active{background:var(--border);transform:scale(.96)}
.key.small{font-size:.85rem;color:var(--muted)}
.key.cancel{font-size:.85rem;color:var(--red)}
.profile-box{background:var(--surface);border:1px solid var(--border);border-radius:22px;width:100%;max-width:360px;padding:22px;display:flex;flex-direction:column;gap:14px;box-shadow:0 16px 40px rgba(0,0,0,.6)}
.profile-title{font-weight:800;font-size:1.1rem}
.profile-label{font-size:.75rem;color:var(--muted)}
.profile-input{width:100%;height:46px;padding:0 12px;border-radius:12px;border:1px solid var(--border);background:var(--card);color:var(--text);font:inherit;font-size:16px;outline:none}
.profile-input:focus{border-color:var(--primary)}
.profile-save{height:44px;border:1px solid var(--primary);border-radius:12px;background:var(--primary);color:#071018;font:inherit;font-weight:800;cursor:pointer}
.profile-divider{height:1px;background:var(--border);margin:2px 0}
.profile-host{min-height:42px;border:1px solid var(--border);border-radius:12px;background:var(--card);color:var(--primary);font:inherit;font-weight:700;cursor:pointer}
.profile-close{align-self:center;border:0;background:transparent;color:var(--muted);font:inherit;font-size:.82rem;cursor:pointer}
.requests-modal{align-items:flex-end!important;padding:0!important;background:rgba(4,7,12,.7)!important}
.requests-sheet{width:100%;max-width:520px;max-height:84vh;display:flex;flex-direction:column;gap:12px;padding:10px 16px calc(18px + env(safe-area-inset-bottom));background:var(--surface);border:1px solid var(--border);border-bottom:0;border-radius:18px 18px 0 0;box-shadow:0 -16px 40px rgba(0,0,0,.55);animation:sheetUp .22s ease-out}
@keyframes sheetUp{from{transform:translateY(24px);opacity:.7}to{transform:translateY(0);opacity:1}}
.sheet-grip{width:38px;height:4px;border-radius:99px;background:var(--border);margin:2px auto 4px}
.requests-sheet .playlist-list{max-height:54vh}
.request-row{display:flex;align-items:center;gap:10px;padding:11px 12px;border:1px solid var(--border);border-radius:12px;background:var(--card)}
.request-row .info{flex:1;min-width:0}
.request-actions{display:flex;gap:6px;flex:0 0 auto}
.request-accept,.request-reject{width:38px;height:38px;padding:0;border-radius:8px;display:inline-flex;align-items:center;justify-content:center;font-size:1rem;cursor:pointer}
.request-accept{border:1px solid rgba(34,197,94,.55);background:rgba(34,197,94,.14);color:#4ade80}
.request-reject{border:1px solid var(--border);background:transparent;color:var(--muted)}
.request-accept:active{background:#22c55e;color:#06121c}
.request-tabs{display:flex;gap:4px;padding:4px;background:var(--card);border:1px solid var(--border);border-radius:10px}
.request-tab{flex:1;min-height:38px;border:0;border-radius:8px;background:transparent;color:var(--muted);font:inherit;font-size:.8rem;font-weight:700;cursor:pointer}
.request-tab.active{background:var(--surface);color:var(--text)}
.request-tab .count{margin-left:4px}
.request-row{flex-direction:column;align-items:stretch;gap:8px}
.request-row .info{width:100%}
.request-row .request-actions{justify-content:flex-end;width:100%}
.request-action-label{min-height:38px;padding:0 12px;border-radius:8px;border:1px solid rgba(34,197,94,.45);background:rgba(34,197,94,.12);color:#4ade80;font:800 .74rem inherit;cursor:pointer}
.request-action-label.reject{border-color:var(--border);background:transparent;color:var(--muted)}
.request-action-label.reject:active{background:rgba(239,68,68,.15);color:#fca5a5}
.request-status{font-size:.7rem;font-weight:800;padding:2px 7px;border-radius:6px}
.request-status.played,.request-status.approved-next{color:#4ade80;background:rgba(34,197,94,.13)}
.request-status.rejected{color:#fca5a5;background:rgba(239,68,68,.13)}
.archive-time{font-size:.7rem;color:var(--sub)}
.request-clear-archive{min-height:40px;border:1px solid rgba(239,68,68,.35);border-radius:8px;background:transparent;color:#fca5a5;font:inherit;font-size:.78rem;font-weight:700;cursor:pointer}
.request-clear-archive:active{background:rgba(239,68,68,.14)}
.request-reject:active{background:rgba(239,68,68,.15);color:#fca5a5}.playlist-box{background:var(--surface);border:1px solid var(--border);border-radius:22px;width:100%;max-width:380px;max-height:80vh;padding:22px;display:flex;flex-direction:column;gap:12px;box-shadow:0 16px 40px rgba(0,0,0,.6)}
.playlist-list{display:flex;flex-direction:column;gap:8px;overflow-y:auto;min-height:60px}
.playlist-loading{display:flex;align-items:center;gap:10px;padding:14px 12px;border:1px solid var(--border);border-radius:12px;background:var(--card);color:var(--muted);font-size:.84rem}
.playlist-loading i{width:18px;height:18px;border:2px solid var(--border);border-top-color:var(--primary);border-radius:50%;animation:playlist-spin .8s linear infinite}
@keyframes playlist-spin{to{transform:rotate(360deg)}}
.playlist-row{display:flex;align-items:center;gap:10px;padding:11px 12px;border:1px solid var(--border);border-radius:12px;background:var(--card)}
.playlist-row .info{flex:1;min-width:0}
.playlist-row .add{min-height:34px;padding:6px 10px}
.confirm-box{background:var(--surface);border:1px solid var(--border);border-radius:22px;width:100%;max-width:360px;padding:22px;display:flex;flex-direction:column;gap:14px;box-shadow:0 16px 40px rgba(0,0,0,.6)}
.confirm-actions{display:flex;gap:8px}
.confirm-actions button{flex:1;min-height:44px;border-radius:12px;font:inherit;font-weight:800;cursor:pointer}
.confirm-cancel{background:var(--card);border:1px solid var(--border);color:var(--text)}
.confirm-delete{background:rgba(239,68,68,.16);border:1px solid rgba(239,68,68,.55);color:#fca5a5}
.confirm-delete:active{background:var(--red);color:#fff}
.toast{position:fixed;bottom:24px;left:50%;transform:translateX(-50%) translateY(50px);background:#1e2430;border:1px solid var(--primary);box-shadow:0 8px 24px rgba(0,0,0,.4);color:#fff;padding:10px 18px;border-radius:99px;font-size:.85rem;font-weight:600;display:flex;align-items:center;gap:8px;z-index:200;opacity:0;transition:all .25s;pointer-events:none;white-space:nowrap}
.toast.show{transform:translateX(-50%) translateY(0);opacity:1}
</style>
</head>
<body>
<div class="app">
  <header>
    <div class="brand"><img class="brand-icon" src="/appicon.png" alt="Tempo Karaoke"><span>Tempo Karaoke</span></div>
    <div class="header-tools">
      <select class="lang-select" id="langSelect" onchange="changeLanguage(this.value)" aria-label="Language"></select>
      <button class="icon-btn requests-btn" id="requestsToggleBtn" type="button" title="Song requests" aria-label="Song requests" onclick="openRequests(event)">
        <svg class="icon-bell" viewBox="0 0 24 24" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9"/><path d="M10 21h4"/></svg>
        <span class="notification-badge" id="requestsCountBadge" hidden>0</span>
      </button>
      <div class="user-profile-dropdown" id="userProfileDropdown">
        <button class="host-badge-btn" id="roleBadge" type="button" onclick="roleClick(event)">
          <span class="host-dot"></span><span class="role-text" id="roleText">Guest</span>
        </button>
        <button class="role-pin" id="rolePin" type="button" onclick="openPinFromBadge(event)">PIN</button>
        <div class="profile-menu" id="profileMenu" role="menu">
          <div class="profile-menu-header" id="profileMenuHeader">Signed in as <strong>Host</strong></div>
          <button class="profile-menu-item" id="openPartySettingsBtn" type="button" onclick="openPartySettings(event)">⚙ Party settings</button>
          <button class="profile-menu-item text-danger" id="logoutBtn" type="button" onclick="logoutProfile(event)">🚪 Sign out</button>
        </div>
      </div>
    </div>
  </header>

  <section class="player">
    <div class="player-top">
      <div class="status"><span class="dot" id="playDot"></span><span id="playStatus" data-i18n="player_no_track">No track</span></div>
    </div>
    <div class="track">
      <div class="track-title" id="trackTitle" data-i18n="player_nothing">Nothing is playing</div>
      <div class="track-sub" id="trackSub" data-i18n="player_choose_on_pc">Choose a song on the computer</div>
    </div>
    <div class="progress">
      <input
        class="timeline"
        id="seek"
        type="range"
        min="0"
        max="1000"
        step="1"
        value="0"
        oninput="seekValueChanged(this.value)"
        onchange="seekCommitted(this.value)">
      <div class="times"><span id="timeCurrent">00:00</span><span id="timeTotal">00:00</span></div>
    </div>
    <div class="host" id="hostControls">
      <div class="buttons">
        <button class="ctrl" onclick="control('prev')"><span>⏮</span><span data-i18n="player_prev">Back</span></button>
        <button class="ctrl play" id="playButton" onclick="control('playpause')"><span id="playIcon">▶</span><span id="playLabel" data-i18n="player_start">Start</span></button>
        <button class="ctrl" onclick="control('stop')"><span>⏹</span><span data-i18n="player_stop">Stop</span></button>
        <button class="ctrl" onclick="control('next')"><span>⏭</span><span data-i18n="player_next">Next</span></button>
      </div>
      <div class="volume">
        <span data-i18n="player_volume">Volume</span>
        <input id="volume" type="range" min="0" max="100" oninput="volumeChanged(this.value)" onchange="control('volume', this.value)">
        <b id="volumeValue">80</b>
      </div>
    </div>
    <div class="guest-note" id="guestNote"><span data-i18n="player_guest_note">Host controls playback.</span> <a data-i18n="player_login_pin" onclick="openPin()">Sign in with PIN</a></div>
  </section>

  <nav class="tabs">
    <button class="tab active" id="tabQueue" onclick="setTab('queue')"><span data-i18n="tab_queue">Queue</span> <span class="count" id="queueCount">0</span></button>
    <button class="tab" id="tabCatalog" onclick="setTab('catalog')"><span data-i18n="tab_catalog">Catalog</span></button>
  </nav>

  <div class="host-queue-tools" id="hostQueueTools">
    <button data-i18n="btn_clear_queue" onclick="openClearQueue()">Clear queue</button>
    <button data-i18n="btn_load_playlist" onclick="openPlaylists()">Load playlist</button>
  </div>

  <div class="search-wrap" id="searchWrap">
    <span class="search-icon">⌕</span>
    <input class="search" id="search" type="search" placeholder="Song or artist" data-i18n-placeholder="search_placeholder" oninput="searchChanged()">
    <button class="clear" id="clearSearch" onclick="clearSearch()" aria-label="Clear search">✕</button>
    <div class="search-suggest" id="searchSuggest" hidden></div>
  </div>

  <div class="list" id="list"></div>
</div>

<div class="modal" id="pinModal">
  <div class="pin-box">
    <div class="pin-title" data-i18n="pin_title">Host sign-in</div>
    <div class="pin-sub" data-i18n="pin_sub">Enter the PIN shown on the computer</div>
    <div class="dots">
      <span class="pin-dot" id="d0"></span><span class="pin-dot" id="d1"></span>
      <span class="pin-dot" id="d2"></span><span class="pin-dot" id="d3"></span>
    </div>
    <div class="pad">
      <button class="key" onclick="pinPress('1')">1</button>
      <button class="key" onclick="pinPress('2')">2</button>
      <button class="key" onclick="pinPress('3')">3</button>
      <button class="key" onclick="pinPress('4')">4</button>
      <button class="key" onclick="pinPress('5')">5</button>
      <button class="key" onclick="pinPress('6')">6</button>
      <button class="key" onclick="pinPress('7')">7</button>
      <button class="key" onclick="pinPress('8')">8</button>
      <button class="key" onclick="pinPress('9')">9</button>
      <button class="key small cancel" data-i18n="common_cancel" onclick="closePin()">Cancel</button>
      <button class="key" onclick="pinPress('0')">0</button>
      <button class="key small" onclick="pinBack()">⌫</button>
    </div>
  </div>
</div>

<div class="modal" id="profileModal">
  <div class="profile-box">
    <div class="profile-title" data-i18n="profile_title">Your name</div>
    <div class="profile-label" data-i18n="profile_sub">How to show you in the queue</div>
    <input class="profile-input" id="guestNameInput" maxlength="32" placeholder="For example, Alex" data-i18n-placeholder="profile_placeholder" autocomplete="nickname">
    <button class="profile-save" data-i18n="common_save" onclick="saveGuestName()">Save</button>
    <button class="profile-close" data-i18n="common_close" onclick="closeProfile()">Close</button>
  </div>
</div>

<div class="modal" id="playlistsModal">
  <div class="playlist-box">
    <div class="profile-title" data-i18n="playlists_title">My playlists</div>
    <div class="profile-label" data-i18n="playlists_sub">Add a playlist to the queue</div>
    <div class="playlist-list" id="playlistList"></div>
    <button class="profile-close" data-i18n="common_close" onclick="closePlaylists()">Close</button>
  </div>
</div>

<div class="modal" id="clearQueueModal">
  <div class="confirm-box">
    <div class="profile-title" data-i18n="clear_title">Clear queue?</div>
    <div class="profile-label" data-i18n="prompt_clear_confirm">Clear queue? All tracks will be removed.</div>
    <div class="confirm-actions">
      <button class="confirm-cancel" data-i18n="common_cancel" onclick="closeClearQueue()">Cancel</button>
      <button class="confirm-delete" data-i18n="clear_delete" onclick="confirmClearQueue()">Delete all</button>
    </div>
  </div>
</div>

<div class="modal requests-modal" id="requestsModal">
  <div class="requests-sheet" role="dialog" aria-modal="true" aria-labelledby="requestsTitle">
    <div class="sheet-grip"></div>
    <div class="profile-title" id="requestsTitle" data-i18n="requests_title">Song requests</div>
    <div class="profile-label" data-i18n="requests_sub">Guests asking to play a song next</div>
    <div class="request-tabs">
      <button class="request-tab active" id="requestsActiveTab" type="button" onclick="setRequestsTab('active')" data-i18n="requests_tab_active">Active</button>
      <button class="request-tab" id="requestsArchiveTab" type="button" onclick="setRequestsTab('archive')" data-i18n="requests_tab_archive">Request archive</button>
    </div>
    <div class="playlist-list" id="requestsList"></div>
    <button class="request-clear-archive" id="clearArchiveBtn" type="button" onclick="clearRequestArchive()" style="display:none" data-i18n="requests_clear_archive">🗑 Clear archive</button>
    <button class="profile-close" data-i18n="common_close" onclick="closeRequests()">Close</button>
  </div>
</div>

<div class="modal" id="adminModal">
  <div class="profile-box">
    <div class="profile-title" data-i18n="admin_title">Party settings</div>
    <div class="profile-label" data-i18n="admin_sub">Safe settings, no playback interruption</div>
    <label class="profile-label"><span data-i18n="admin_countdown">Countdown before next track</span>
      <input id="adminCountdown" type="checkbox">
    </label>
    <label class="profile-label"><span data-i18n="admin_countdown_seconds">Countdown seconds</span>
      <input class="profile-input" id="adminSeconds" type="number" min="3" max="120">
    </label>
    <label class="profile-label"><span data-i18n="admin_limit_guest">Requests per guest</span>
      <input class="profile-input" id="adminGuestLimit" type="number" min="1" max="20">
    </label>
    <label class="profile-label"><span data-i18n="admin_limit_total">Pending requests total</span>
      <input class="profile-input" id="adminTotalLimit" type="number" min="1" max="200">
    </label>
    <label class="profile-label"><span data-i18n="admin_theme">Theme</span>
      <select class="profile-input" id="adminTheme">
        <option value="system" data-i18n="admin_theme_system">System</option>
        <option value="dark" data-i18n="admin_theme_dark">Dark</option>
        <option value="light" data-i18n="admin_theme_light">Light</option>
      </select>
    </label>
    <label class="profile-label"><span data-i18n="admin_language">Language</span>
      <select class="profile-input" id="adminLanguage"></select>
    </label>
    <button class="profile-save" data-i18n="common_save" onclick="saveAdminSettings()">Save</button>
    <button class="profile-close" data-i18n="common_close" onclick="closeAdmin()">Close</button>
  </div>
</div>

<div class="toast" id="toast"><span id="toastIcon">✦</span><span id="toastText"></span></div>

<script>
const token = new URLSearchParams(location.search).get('token') || '';
const $ = id => document.getElementById(id);
let languageOptions = [{ tag: 'en-US', nativeName: 'English' }];
let fallbackTag = 'en-US';
let fallbackMessages = {};
let language = localStorage.getItem('lang') || navigator.language;
let messages = {};
const FALLBACK_MESSAGES = {
  "header_host": "Host",
  "header_guest": "Guest",
  "header_exit_host": "Exit host mode",
  "header_requests": "Song requests",
  "header_admin": "Admin settings",
  "header_admin_badge": "Administrator",
  "profile_signed_in": "Signed in as",
  "profile_party_settings": "Party settings",
  "profile_sign_out": "Sign out",
  "header_admin_short": "Admin",
  "btn_ask_next": "Ask next",
  "btn_asked": "Requested",
  "requests_title": "Notification center",
  "requests_sub": "Guests asking to play a song next",
  "requests_empty": "No pending requests.",
  "requests_from": "From {name}",
  "requests_accept": "Play next",
  "requests_play": "Play now",
  "requests_reject": "Reject",
  "requests_done": "Request handled",
  "requests_failed": "Could not update requests",
  "requests_tab_active": "Active requests",
  "requests_tab_archive": "Request archive",
  "requests_archive_empty": "No processed requests yet.",
  "requests_status_played": "Played immediately",
  "requests_status_approved": "Approved (next)",
  "requests_status_rejected": "Rejected",
  "requests_clear_archive": "🗑 Clear archive",  "requests_limit": "Request limit reached",
  "requests_sent": "Request sent to the host",
  "admin_title": "Party settings",
  "admin_sub": "Safe settings, no playback interruption",
  "admin_countdown": "Countdown before next track",
  "admin_countdown_seconds": "Countdown seconds",
  "admin_limit_guest": "Requests per guest",
  "admin_limit_total": "Pending requests total",
  "admin_pin_title": "Administrator sign-in",
  "admin_pin_sub": "Enter the administrator PIN shown on the computer",
  "admin_saved": "Settings saved",
  "admin_save_failed": "Could not save settings",
  "admin_load_failed": "Could not load settings",
  "toast_admin_on": "Administrator mode enabled",
  "player_now_playing": "Now Playing",
  "player_pause": "Pause",
  "player_play": "Play",
  "player_start": "Start",
  "player_stop": "Stop",
  "player_prev": "Back",
  "player_next": "Next",
  "player_volume": "Volume",
  "player_no_track": "No track",
  "player_nothing": "Nothing is playing",
  "player_choose_on_pc": "Choose a song on the computer",
  "player_guest_note": "Host controls playback.",
  "player_login_pin": "Sign in with PIN",
  "tab_queue": "Queue",
  "tab_catalog": "Catalog",
  "btn_add_to_queue": "+ Add to queue",
  "btn_added": "Added",
  "btn_clear_queue": "Clear queue",
  "btn_load_playlist": "Load playlist",
  "prompt_clear_confirm": "Clear queue? All tracks will be removed.",
  "clear_title": "Clear queue?",
  "clear_delete": "Delete all",
  "common_cancel": "Cancel",
  "common_save": "Save",
  "common_close": "Close",
  "search_placeholder": "Song or artist",
  "search_clear": "Clear search",
  "search_failed": "Search failed.",
  "search_empty": "Nothing found.",
  "search_clarify": "Please refine your search: enter a song title or artist name",
  "catalog_prompt": "Enter a song title or artist.",
  "catalog_play_now": "▶ Play now",
  "empty_queue": "The queue is empty. Open the catalog and add a song.",
  "queue_play_now": "Play now",
  "queue_drag": "Drag to reorder",
  "queue_remove": "Remove",
  "queue_next_tracks": "NEXT TRACKS",
  "queue_you": "You ({name})",
  "queue_computer": "Computer",
  "pin_title": "Host sign-in",
  "pin_sub": "Enter the PIN shown on the computer",
  "profile_title": "Your name",
  "profile_sub": "How to show you in the queue",
  "profile_placeholder": "For example, Alex",
  "playlists_title": "My playlists",
  "playlists_sub": "Add a playlist to the queue",
  "playlists_empty": "No playlists yet.",
  "playlists_loading": "Loading playlists…",
  "playlists_error": "Could not load playlists.",
  "playlist_tracks_one": "{count} track",
  "playlist_tracks_few": "{count} tracks",
  "playlist_tracks_many": "{count} tracks",
  "playlist_tracks_other": "{count} tracks",
  "toast_added": "Song added to the queue",
  "toast_added_named": "Added: {name}",
  "toast_add_failed": "Could not add the song",
  "toast_track_missing": "Track not found in the queue",
  "toast_track_started": "Track started",
  "toast_track_start_failed": "Could not start the track",
  "toast_remove_failed": "Could not remove the track",
  "toast_move_failed": "Could not change the queue",
  "toast_command_failed": "Command failed",
  "toast_folder": "Folder: {folder}",
  "toast_cleared": "Queue cleared",
  "toast_clear_failed": "Could not clear the queue",
  "toast_playlist_missing": "Playlist is empty or not found",
  "toast_playlist_added": "Playlist “{name}” added",
  "toast_playlist_add_failed": "Could not add the playlist",
  "toast_host_exited": "Host mode disabled",
  "toast_host_exit_failed": "Could not exit host mode",
  "toast_name_saved": "You are in the queue as {name}",
  "toast_name_cleared": "Guest name cleared",
  "toast_host_on": "Host mode enabled",
  "toast_pin_invalid": "Invalid PIN",
  "toast_pin_failed": "Could not verify PIN",
  "page_title": "Tempo Karaoke — remote",
  "player_paused": "Paused",
  "playlist_tracks": "{count} tracks"
};
let state = null;
let canControl = false;
let tab = 'queue';
let pin = '';
let pinMode = 'host';
let requestsTab = 'active';
let canAdmin = false;
let searchTimer = null;
let lastQueueSignature = null;
let seekDragging = false;
let socket = null;
let reconnectTimer = null;
let reconnectDelay = 1000;
let guestName = (localStorage.getItem('guest_name') || '').trim();

function normalizeLanguage(value) {
  const raw = String(value || '').trim().toLowerCase();
  if (!raw) return fallbackTag;
  const exact = languageOptions.find(option => option.tag.toLowerCase() === raw);
  if (exact) return exact.tag;
  const primary = raw.split('-')[0];
  const group = languageOptions.find(option => option.tag.toLowerCase().split('-')[0] === primary);
  return group ? group.tag : fallbackTag;
}

async function loadLocaleManifest() {
  try {
    const response = await fetch('/locale-manifest.json', { cache: 'no-cache' });
    if (!response.ok) throw new Error(String(response.status));
    const manifest = await response.json();
    if (Array.isArray(manifest.languages) && manifest.languages.length > 0) {
      languageOptions = manifest.languages.map(entry => ({
        tag: entry.tag,
        nativeName: entry.nativeName || entry.tag
      }));
      fallbackTag = manifest.fallback || 'en-US';
    }
  } catch {
    // Keep the built-in English entry; the remote still works.
  }
  populateLanguageSelectors();
}

function populateLanguageSelectors() {
  ['langSelect', 'adminLanguage'].forEach(id => {
    const select = $(id);
    if (!select) return;
    const previous = select.value;
    select.innerHTML = '';
    languageOptions.forEach(option => {
      const element = document.createElement('option');
      element.value = option.tag;
      element.textContent = languageCode(option.tag);
      element.title = option.nativeName;
      select.appendChild(element);
    });
    select.value = previous && languageOptions.some(option => option.tag === previous)
      ? previous
      : language;
  });
}

function languageCode(tag) {
  const primary = String(tag || '').split('-')[0].trim();
  return (primary || String(tag || '')).toUpperCase();
}

async function fetchCatalog(tag) {
  try {
    const response = await fetch('/locales/' + encodeURIComponent(tag) + '.json', { cache: 'no-cache' });
    if (!response.ok) throw new Error(String(response.status));
    return await response.json();
  } catch {
    return {};
  }
}

async function loadLanguage(value, persist) {
  language = normalizeLanguage(value);
  if (persist) localStorage.setItem('lang', language);
  const english = await fetchCatalog(fallbackTag);
  const current = language === fallbackTag ? english : await fetchCatalog(language);
  fallbackMessages = english;
  messages = Object.assign({}, english, current);
  document.documentElement.lang = language;
  document.title = t('page_title');
  const select = $('langSelect');
  if (select) select.value = language;
  const adminSelect = $('adminLanguage');
  if (adminSelect) adminSelect.value = language;
  applyStaticTranslations();
}

function t(key, params) {
  let value = messages[key] ?? fallbackMessages[key] ?? FALLBACK_MESSAGES[key];
  if (typeof value !== 'string' || value.length === 0) {
    value = key;
  }
  if (params) {
    Object.keys(params).forEach(name => {
      value = value.split('{' + name + '}').join(String(params[name]));
    });
  }
  return value;
}

function tp(base, count, params) {
  const category = new Intl.PluralRules(language).select(count);
  const merged = Object.assign({ count }, params || {});
  return t(base + '_' + category, merged);
}

function applyStaticTranslations() {
  document.querySelectorAll('[data-i18n]').forEach(element => {
    const key = element.getAttribute('data-i18n');
    if (typeof messages[key] === 'string') element.textContent = messages[key];
  });
  document.querySelectorAll('[data-i18n-placeholder]').forEach(element => {
    const key = element.getAttribute('data-i18n-placeholder');
    if (typeof messages[key] === 'string') element.setAttribute('placeholder', messages[key]);
  });
  const search = $('clearSearch');
  if (search) search.setAttribute('aria-label', t('search_clear'));
  const languageSelect = $('langSelect');
  if (languageSelect) languageSelect.setAttribute('aria-label', t('admin_language'));
  const bell = $('requestsToggleBtn');
  if (bell) bell.setAttribute('title', t('header_requests'));
  const settings = $('openPartySettingsBtn');
  if (settings) settings.textContent = '⚙ ' + t('profile_party_settings');
  const logout = $('logoutBtn');
  if (logout) logout.textContent = '🚪 ' + t('profile_sign_out');
  if (typeof canControl !== 'undefined') {
    const header = $('profileMenuHeader');
    if (header) {
      header.innerHTML = t('profile_signed_in') + ' <strong>' +
        (canAdmin ? t('header_admin_badge') : (canControl ? t('header_host') : t('header_guest'))) + '</strong>';
    }
  }
}

async function changeLanguage(value) {
  await loadLanguage(value, true);
  if (state) apply(state);
  render();
}

function hasTrack() {
  if (!state || !state.title) return false;
  const title = String(state.title).trim();
  return title.length > 0 &&
    title !== 'Ничего не играет' &&
    title !== 'Nothing is playing' &&
    title !== t('player_nothing');
}

function renderPlayback() {
  if (!state) return;
  const playing = !!state.isPlaying && hasTrack();
  $('playIcon').textContent = playing ? '❚❚' : '▶';
  $('playLabel').textContent = playing ? t('player_pause') : t('player_start');
  $('playStatus').textContent = !hasTrack()
    ? t('player_no_track')
    : (playing ? t('player_now_playing') : t('player_paused'));
  $('playDot').style.background = playing ? 'var(--primary)' : 'var(--amber)';
  $('timeTotal').textContent = fmt(state.lengthMs);
  $('volume').value = state.volume || 0;
  $('volumeValue').textContent = state.volume || 0;

  // Пока пользователь тянет ползунок, не перебиваем его положение
  // очередным снимком состояния с компьютера.
  if (!seekDragging) {
    $('timeCurrent').textContent = fmt(state.positionMs);
    const position = state.lengthMs > 0
      ? Math.max(0, Math.min(1000, Math.round((state.positionMs || 0) / state.lengthMs * 1000)))
      : 0;
    $('seek').value = String(position);
    $('seek').style.setProperty('--seek', (position / 10) + '%');
  }
}


async function api(path, options) {
  const sep = path.includes('?') ? '&' : '?';
  const response = await fetch(path + sep + 'token=' + encodeURIComponent(token), {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    ...options
  });
  if (!response.ok) throw new Error(String(response.status));
  return response.json();
}

function fmt(ms) {
  const total = Math.max(0, Math.floor((ms || 0) / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return (h > 0 ? h + ':' : '') + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
}

function toast(text, icon) {
  $('toastText').textContent = text;
  $('toastIcon').textContent = icon || '✦';
  $('toast').classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => $('toast').classList.remove('show'), 2400);
}

function apply(data) {
  if (!data) return;
  state = data.state || data;
  state.clientId = data.clientId || state.clientId || '';
  state.aliasName = data.aliasName || '';
  state.fallbackName = data.fallbackName || '';
  canControl = !!data.canControl;
  canAdmin = !!data.canAdmin;

  $('roleBadge').classList.toggle('is-host', canControl || canAdmin);
  $('roleText').textContent = canAdmin
    ? '⚙ ' + t('header_admin_badge')
    : (canControl
      ? '👑 ' + t('header_host')
      : (state.aliasName || guestName || state.fallbackName || t('header_guest')));
  $('rolePin').style.display = canControl ? 'none' : 'inline-flex';
  $('openPartySettingsBtn').style.display = canAdmin ? '' : 'none';
  $('requestsToggleBtn').style.display = (canControl || canAdmin) ? 'inline-flex' : 'none';
  $('profileMenuHeader').innerHTML = t('profile_signed_in') + ' <strong>' +
    (canAdmin ? t('header_admin_badge') : (canControl ? t('header_host') : t('header_guest'))) + '</strong>';
  $('openPartySettingsBtn').textContent = '⚙ ' + t('profile_party_settings');
  $('logoutBtn').textContent = '🚪 ' + t('profile_sign_out');
  if (!canControl && !canAdmin) closeProfileMenu();
  $('hostControls').classList.toggle('visible', canControl);
  $('hostQueueTools').classList.toggle('visible', canControl);
  if (canControl) preloadPlaylists();
  $('guestNote').style.display = canControl ? 'none' : 'block';
  if (canControl || canAdmin) refreshRequestBadge();
  else {
    $('requestsCountBadge').hidden = true;
    $('requestsToggleBtn').style.display = 'none';
  }

  const title = hasTrack() ? (state.title || t('player_nothing')) : t('player_nothing');
  $('trackTitle').textContent = title;
  $('trackSub').textContent = hasTrack() ? (state.subtitle || '') : t('player_choose_on_pc');
  renderPlayback();

  const queue = state.queue || [];
  $('queueCount').textContent = queue.length;
  const signature = queue.map(item => item.path + ':' + (item.requesterId || '') + ':' + (item.requester || '')).join('|') + '|' + queue.length + '|' +
    (state.currentIndex ?? -1) + '|' + canControl + '|' + (guestName || '');
  if (tab === 'queue' && signature !== lastQueueSignature) {
    lastQueueSignature = signature;
    render();
  }
}

function render() {
  const queue = (state && state.queue) || [];
  $('queueCount').textContent = queue.length;
  $('tabQueue').classList.toggle('active', tab === 'queue');
  $('tabCatalog').classList.toggle('active', tab === 'catalog');
  $('searchWrap').classList.toggle('visible', tab === 'catalog');
  const list = $('list');
  list.replaceChildren();

  if (tab === 'queue') renderQueue(list, queue);
  else renderCatalog(list);
}

function renderQueue(list, queue) {
  const currentInQueue = Number.isInteger(state.currentIndex) &&
    state.currentIndex >= 0 && state.currentIndex < queue.length;

  if (!currentInQueue && state.title && hasTrack()) {
    const banner = document.createElement('div');
    banner.className = 'now-banner';
    banner.textContent = t('player_now_playing') + ': ' + state.title;
    list.append(banner);
  }

  if (!queue.length) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = t('empty_queue');
    list.append(empty);
    return;
  }

  queue.forEach((item, index) => {
    const isPlaying = currentInQueue && index === state.currentIndex;
    const isPast = currentInQueue && index < state.currentIndex;
    const isMine = !!state.clientId && item.requesterId === state.clientId;
    const card = document.createElement('div');
    card.className = 'item' + (isPlaying ? ' playing' : '') +
      (isMine ? ' mine' : '') + (isPast ? ' past' : '');
    card.draggable = canControl;
    card.dataset.index = String(index);
    if (canControl) {
      card.addEventListener('dragstart', e => { e.dataTransfer.setData('text', String(index)); card.classList.add('drag'); });
      card.addEventListener('dragend', () => card.classList.remove('drag'));
      card.addEventListener('dragover', e => { e.preventDefault(); card.classList.add('drag-over'); });
      card.addEventListener('dragleave', () => card.classList.remove('drag-over'));
      card.addEventListener('drop', e => {
        e.preventDefault();
        card.classList.remove('drag-over');
        const from = Number(e.dataTransfer.getData('text'));
        if (!Number.isNaN(from) && from !== index) moveQueue(from, index);
      });
    }

    let num;
    if (isPlaying) {
      num = document.createElement('span');
      num.className = 'eq';
      num.innerHTML = '<span></span><span></span><span></span>';
    } else if (canControl) {
      num = document.createElement('button');
      num.className = 'num num-play';
      num.title = t('queue_play_now');
      num.onclick = () => playNow(item.path);
      const value = document.createElement('span');
      value.className = 'num-value';
      value.textContent = String(index + 1);
      const glyph = document.createElement('span');
      glyph.className = 'num-glyph';
      glyph.textContent = '▶';
      num.append(value, glyph);
    } else {
      num = document.createElement('span');
      num.className = 'num';
      num.textContent = String(index + 1);
    }

    const info = document.createElement('div');
    info.className = 'info';
    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = item.title;
    const sub = document.createElement('div');
    sub.className = 'sub';
    const duration = document.createElement('span');
    duration.textContent = item.duration || '';
    const requester = document.createElement('span');
    requester.className = 'badge';
    requester.textContent = isMine
      ? t('queue_you', { name: item.requester || t('header_guest') })
      : (item.requester === 'Компьютер' ? t('queue_computer') : (item.requester || t('queue_computer')));
    sub.append(duration, requester);
    info.append(name, sub);
    card.append(num, info);

    if (canControl) {
      const actions = document.createElement('div');
      actions.className = 'queue-actions';
      const handle = document.createElement('span');
      handle.className = 'handle';
      handle.textContent = '⠿';
      handle.title = t('queue_drag');
      const remove = document.createElement('button');
      remove.className = 'delete';
      remove.textContent = '🗑';
      remove.title = t('queue_remove');
      remove.onclick = () => removeQueue(index);
      actions.append(handle, remove);
      card.append(actions);
    }

    list.append(card);
    if (isPlaying && index < queue.length - 1) {
      const divider = document.createElement('div');
      divider.className = 'queue-divider';
      divider.textContent = t('queue_next_tracks');
      list.append(divider);
    }
  });
}

async function renderCatalog(list) {
  const query = $('search').value.trim();
  if (!query) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = t('catalog_prompt');
    list.append(empty);
    return;
  }

  let results = [];
  let needsClarification = false;
  try {
    const payload = await api('/api/search?q=' + encodeURIComponent(query));
    if (Array.isArray(payload)) {
      results = payload;
    } else {
      results = payload.items || [];
      needsClarification = payload.needsClarification === true;
    }
  } catch {
    const error = document.createElement('div');
    error.className = 'empty';
    error.textContent = t('search_failed');
    list.append(error);
    return;
  }

  if (!results.length) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = needsClarification ? t('search_clarify') : t('search_empty');
    list.append(empty);
    return;
  }

  results.forEach(item => {
    const card = document.createElement('div');
    card.className = 'item catalog-card';
    const info = document.createElement('div');
    info.className = 'info';
    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = item.title;
    const sub = document.createElement('div');
    sub.className = 'sub';
    const artist = document.createElement('span');
    artist.className = 'artist';
    artist.textContent = item.artist || '';
    const duration = document.createElement('span');
    duration.className = 'duration';
    duration.textContent = item.duration || '';
    sub.append(artist, duration);
    info.append(name, sub);
    const actions = document.createElement('div');
    actions.className = 'catalog-actions';
    if (canControl) {
      const play = document.createElement('button');
      play.className = 'catalog-play';
      play.type = 'button';
      play.textContent = '▶';
      play.title = t('catalog_play_now');
      play.setAttribute('aria-label', t('catalog_play_now'));
      play.onclick = () => playNow(item.path);
      actions.append(play);
    }
    const add = document.createElement('button');
    add.className = 'catalog-add';
    add.type = 'button';
    add.textContent = '+';
    add.title = t('btn_add_to_queue');
    add.setAttribute('aria-label', t('btn_add_to_queue'));
    add.onclick = () => enqueue(item.path, add);
    actions.append(add);
    const ask = document.createElement('button');
    ask.className = 'catalog-next';
    ask.type = 'button';
    ask.textContent = '⏭';
    ask.title = t('btn_ask_next');
    ask.setAttribute('aria-label', t('btn_ask_next'));
    ask.onclick = () => requestNext(item.path, ask);
    actions.append(ask);
    card.append(info, actions);
    list.append(card);
  });
}

function setTab(value) {
  tab = value;
  render();
}

async function enqueue(path, button) {
  if (button && button.disabled) return;
  try {
    await api('/api/queue', {
      method: 'POST',
      body: JSON.stringify({ path, requesterName: guestName || null })
    });
    if (button) {
      button.disabled = true;
      button.classList.add('added');
      button.textContent = '✓';
      setTimeout(() => {
        button.disabled = false;
        button.classList.remove('added');
        button.textContent = '+';
      }, 2000);
    }
    const count = $('queueCount');
    count.classList.remove('bump');
    void count.offsetWidth;
    count.classList.add('bump');
    setTimeout(() => count.classList.remove('bump'), 400);
    toast(guestName ? t('toast_added_named', { name: guestName }) : t('toast_added'), '🎤');
    await refresh();
  } catch {
    toast(t('toast_add_failed'), '⚠');
  }
}

async function playNow(path) {
  try {
    const result = await api('/api/queue/play-now', {
      method: 'POST',
      body: JSON.stringify({ trackId: path })
    });
    if (!result.ok) {
      toast(t('toast_track_missing'), '⚠');
      return;
    }
    toast(t('toast_track_started'), '▶');
    await refresh();
  } catch {
    toast(t('toast_track_start_failed'), '⚠');
  }
}

async function removeQueue(index) {
  try {
    await api('/api/remove', { method: 'POST', body: JSON.stringify({ index }) });
    await refresh();
  } catch {
    toast(t('toast_remove_failed'), '⚠');
  }
}

async function moveQueue(from, to) {
  try {
    await api('/api/move', { method: 'POST', body: JSON.stringify({ from, to }) });
    await refresh();
  } catch {
    toast(t('toast_move_failed'), '⚠');
  }
}

async function control(action, value) {
  if (!canControl) return;

  // Кнопка «Старт/Пауза» должна реагировать сразу, не ожидая round-trip.
  if (action === 'playpause' && state) {
    state.isPlaying = !state.isPlaying;
    renderPlayback();
  }

  try {
    await api('/api/control', { method: 'POST', body: JSON.stringify({ action, value: Number(value) || 0 }) });
    await refresh();
  } catch {
    toast(t('toast_command_failed'), '⚠');
    await refresh();
  }
}

function volumeChanged(value) {
  $('volumeValue').textContent = value;
}

function seekValueChanged(value) {
  seekDragging = true;
  $('timeCurrent').textContent = fmt((state?.lengthMs || 0) * value / 1000);
  $('seek').style.setProperty('--seek', (Number(value) / 10) + '%');
}

async function seekCommitted(value) {
  if (!canControl) {
    seekDragging = false;
    return;
  }

  try {
    await api('/api/control', {
      method: 'POST',
      body: JSON.stringify({ action: 'seek', value: Number(value) || 0 })
    });
    seekDragging = false;
    await refresh();
  } catch {
    seekDragging = false;
    toast(t('toast_command_failed'), '⚠');
  }
}

function showFolder(item) {
  toast(t('toast_folder', { folder: item.folder || '—' }), '📁');
}

function openPinFromBadge(event) {
  event.stopPropagation();
  openPin();
}

function openAdminPin(event) {
  if (event) event.stopPropagation();
  closeProfileMenu();
  pinMode = 'host';
  $('pinModal').querySelector('.pin-title').textContent = t('pin_title');
  $('pinModal').querySelector('.pin-sub').textContent = t('pin_sub');
  pin = '';
  updateDots();
  $('pinModal').classList.add('active');
}

function roleClick(event) {
  if (event) event.stopPropagation();
  if (canControl || canAdmin) {
    $('profileMenu').classList.toggle('open');
    return;
  }
  openProfile();
}

function closeProfileMenu() {
  $('profileMenu').classList.remove('open');
}

function openPartySettings(event) {
  if (event) event.stopPropagation();
  closeProfileMenu();
  if (canAdmin) {
    openAdmin();
  } else {
    openAdminPin(event);
  }
}

async function logoutProfile(event) {
  if (event) event.stopPropagation();
  closeProfileMenu();
  try {
    if (canAdmin) await api('/api/admin/exit', { method: 'POST', body: '{}' });
    if (canControl) await api('/api/host/exit', { method: 'POST', body: '{}' });
    canControl = false;
    canAdmin = false;
    toast(t('toast_host_exited'), '👋');
    await refresh();
    render();
  } catch {
    toast(t('toast_host_exit_failed'), '⚠');
  }
}

function openClearQueue() {
  if (!canControl) return;
  $('clearQueueModal').classList.add('active');
}

function closeClearQueue() {
  $('clearQueueModal').classList.remove('active');
}

async function refreshRequestBadge() {
  const button = $('requestsToggleBtn');
  const badge = $('requestsCountBadge');
  if (!button || !badge) return;
  if (!canControl && !canAdmin) {
    badge.hidden = true;
    return;
  }
  try {
    const requests = await api('/api/requests');
    button.style.display = 'inline-flex';
    badge.textContent = String(requests.length);
    badge.hidden = requests.length === 0;
    button.setAttribute('aria-label', t('header_requests') + ' (' + requests.length + ')');
  } catch {
    badge.hidden = true;
  }
}

async function openRequests(event) {
  if (event) event.stopPropagation();
  if (!canControl && !canAdmin) return;
  closeProfileMenu();
  requestsTab = 'active';
  $('requestsModal').classList.add('active');
  await loadRequests();
}

function closeRequests() {
  $('requestsModal').classList.remove('active');
}

async function loadRequests() {
  const list = $('requestsList');
  list.replaceChildren();
  const archiveMode = requestsTab === 'archive';
  $('requestsActiveTab').classList.toggle('active', !archiveMode);
  $('requestsArchiveTab').classList.toggle('active', archiveMode);
  $('clearArchiveBtn').style.display = archiveMode ? '' : 'none';

  try {
    if (archiveMode) {
      const archive = await api('/api/requests/archive');
      if (!archive.length) {
        const empty = document.createElement('div');
        empty.className = 'empty';
        empty.textContent = t('requests_archive_empty');
        list.append(empty);
        return;
      }

      archive.forEach(request => {
        const row = document.createElement('div');
        row.className = 'request-row archive-row';
        const info = document.createElement('div');
        info.className = 'info';
        const title = document.createElement('div');
        title.className = 'name';
        title.textContent = request.title;
        const sub = document.createElement('div');
        sub.className = 'sub';
        const from = document.createElement('span');
        from.textContent = t('requests_from', { name: request.requester || t('header_guest') });
        const time = document.createElement('span');
        time.className = 'archive-time';
        time.textContent = formatArchiveTime(request.closedUtcTicks);
        sub.append(from, time);
        info.append(title, sub);
        const status = document.createElement('span');
        status.className = 'request-status ' + (request.status || 'rejected');
        status.textContent = archiveStatusLabel(request.status);
        row.append(info, status);
        list.append(row);
      });
      return;
    }

    const requests = await api('/api/requests');
    if (!requests.length) {
      const empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = t('requests_empty');
      list.append(empty);
      return;
    }

    requests.forEach(request => {
      const row = document.createElement('div');
      row.className = 'request-row';
      const info = document.createElement('div');
      info.className = 'info';
      const title = document.createElement('div');
      title.className = 'name';
      title.textContent = request.title;
      const sub = document.createElement('div');
      sub.className = 'sub';
      const from = document.createElement('span');
      from.textContent = t('requests_from', { name: request.requester || t('header_guest') });
      const requested = document.createElement('span');
      requested.className = 'archive-time';
      requested.textContent = formatArchiveTime(request.createdUtcTicks);
      sub.append(from, requested);
      info.append(title, sub);

      const actions = document.createElement('div');
      actions.className = 'request-actions';
      const play = document.createElement('button');
      play.className = 'request-action-label';
      play.type = 'button';
      play.textContent = '▶ ' + t('requests_play');
      play.onclick = () => resolveRequest(request.id, 'play');
      const next = document.createElement('button');
      next.className = 'request-action-label';
      next.type = 'button';
      next.textContent = '⏭ ' + t('requests_accept');
      next.onclick = () => resolveRequest(request.id, 'next');
      const reject = document.createElement('button');
      reject.className = 'request-action-label reject';
      reject.type = 'button';
      reject.textContent = '✕ ' + t('requests_reject');
      reject.onclick = () => resolveRequest(request.id, 'reject');
      actions.append(play, next, reject);
      row.append(info, actions);
      list.append(row);
    });
  } catch {
    const error = document.createElement('div');
    error.className = 'empty';
    error.textContent = t('requests_failed');
    list.append(error);
  }
}

function archiveStatusLabel(status) {
  if (status === 'played') return '🟢 ' + t('requests_status_played');
  if (status === 'approved-next') return '🟢 ' + t('requests_status_approved');
  return '🔴 ' + t('requests_status_rejected');
}

function formatArchiveTime(ticks) {
  if (!ticks) return '';
  const date = new Date(Number(ticks) / 10000 - 62135596800000);
  return date.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
}

function setRequestsTab(value) {
  requestsTab = value;
  loadRequests();
}

async function clearRequestArchive() {
  try {
    await api('/api/requests/archive/clear', { method: 'POST', body: '{}' });
    await loadRequests();
  } catch {
    toast(t('requests_failed'), '⚠');
  }
}

async function resolveRequest(id, action) {
  try {
    const result = await api('/api/requests/resolve', {
      method: 'POST',
      body: JSON.stringify({ id, action })
    });
    if (!result.ok) throw new Error('failed');
    toast(t('requests_done'), '✓');
    await refresh();
    await loadRequests();
    await refreshRequestBadge();
  } catch {
    toast(t('requests_failed'), '⚠');
  }
}

async function requestNext(path, button) {
  if (button && button.disabled) return;
  try {
    const result = await api('/api/requests', {
      method: 'POST',
      body: JSON.stringify({ path, requesterName: guestName || null })
    });
    if (!result.ok) {
      toast(t('requests_limit'), '⚠');
      return;
    }
    if (button) {
      button.disabled = true;
      button.classList.add('btn-success');
      button.textContent = '✓ ' + t('btn_asked');
    }
    toast(t('requests_sent'), '🙋');
  } catch {
    toast(t('requests_failed'), '⚠');
  }
}

async function openAdmin() {
  if (!canAdmin) {
    openAdminPin();
    return;
  }

  $('adminModal').classList.add('active');
  try {
    const settings = await api('/api/admin/settings');
    $('adminCountdown').checked = !!settings.countdownEnabled;
    $('adminSeconds').value = settings.countdownSeconds;
    $('adminGuestLimit').value = settings.maxRequestsPerGuest;
    $('adminTotalLimit').value = settings.maxPendingRequests;
    $('adminTheme').value = settings.theme || 'system';
    $('adminLanguage').value = settings.language || language;
  } catch {
    toast(t('admin_load_failed'), '⚠');
  }
}

function closeAdmin() {
  $('adminModal').classList.remove('active');
}

async function saveAdminSettings() {
  try {
    const result = await api('/api/admin/settings', {
      method: 'POST',
      body: JSON.stringify({
        countdownEnabled: $('adminCountdown').checked,
        countdownSeconds: Number($('adminSeconds').value) || 10,
        maxRequestsPerGuest: Number($('adminGuestLimit').value) || 2,
        maxPendingRequests: Number($('adminTotalLimit').value) || 20,
        theme: $('adminTheme').value || 'system',
        language: $('adminLanguage').value || language
      })
    });
    if (!result.ok) throw new Error('failed');
    toast(t('admin_saved'), '✓');
    closeAdmin();
    await refresh();
  } catch {
    toast(t('admin_save_failed'), '⚠');
  }
}

async function confirmClearQueue() {
  closeClearQueue();
  try {
    await api('/api/queue/clear', { method: 'POST', body: '{}' });
    toast(t('toast_cleared'), '🧹');
    await refresh();
  } catch {
    toast(t('toast_clear_failed'), '⚠');
  }
}

async function openPlaylists() {
  if (!canControl) return;
  const list = $('playlistList');
  list.replaceChildren();
  $('playlistsModal').classList.add('active');

  if (playlistsCache) {
    renderPlaylists(playlistsCache);
  } else {
    list.append(loadingPlaylists());
  }

  try {
    const playlists = await loadPlaylists();
    renderPlaylists(playlists);
  } catch {
    list.replaceChildren();
    const error = document.createElement('div');
    error.className = 'empty';
    error.textContent = t('playlists_error');
    list.append(error);
  }
}

let playlistsCache = null;
let playlistsCacheAt = 0;
let playlistsRequest = null;

function loadingPlaylists() {
  const row = document.createElement('div');
  row.className = 'playlist-loading';
  const spinner = document.createElement('i');
  const text = document.createElement('span');
  text.textContent = t('playlists_loading');
  row.append(spinner, text);
  return row;
}

function loadPlaylists(force = false) {
  if (!force && playlistsCache && Date.now() - playlistsCacheAt < 30000) {
    return Promise.resolve(playlistsCache);
  }
  if (playlistsRequest) return playlistsRequest;
  playlistsRequest = api('/api/playlists')
    .then(playlists => {
      playlistsCache = playlists;
      playlistsCacheAt = Date.now();
      return playlists;
    })
    .finally(() => {
      playlistsRequest = null;
    });
  return playlistsRequest;
}

function preloadPlaylists() {
  if (canControl && !playlistsCache) {
    loadPlaylists().catch(() => {});
  }
}

function renderPlaylists(playlists) {
  const list = $('playlistList');
  list.replaceChildren();
  if (!playlists.length) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = t('playlists_empty');
    list.append(empty);
    return;
  }

  playlists.forEach(playlist => {
    const row = document.createElement('div');
    row.className = 'playlist-row';
    const info = document.createElement('div');
    info.className = 'info';
    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = playlist.name;
    const sub = document.createElement('div');
    sub.className = 'sub';
    const count = playlist.count || 0;
    sub.textContent = tp('playlist_tracks', count);
    info.append(name, sub);
    const add = document.createElement('button');
    add.className = 'add';
    add.textContent = t('btn_add_to_queue');
    add.onclick = () => addPlaylist(playlist.id, playlist.name, add);
    row.append(info, add);
    list.append(row);
  });
}

function closePlaylists() {
  $('playlistsModal').classList.remove('active');
}

async function addPlaylist(id, name, button) {
  if (button.disabled) return;
  try {
    const result = await api('/api/playlists/add', {
      method: 'POST',
      body: JSON.stringify({ id })
    });
    if (!result.ok) {
      toast(t('toast_playlist_missing'), '⚠');
      return;
    }
    button.disabled = true;
    button.textContent = '✓';
    toast(t('toast_playlist_added', { name }), '🎵');
    await refresh();
  } catch {
    toast(t('toast_playlist_add_failed'), '⚠');
  }
}

async function exitHost() {
  closeProfileMenu();
  try {
    await api('/api/host/exit', { method: 'POST', body: '{}' });
    canControl = false;
    $('roleBadge').classList.remove('is-host');
    $('hostControls').classList.remove('visible');
    $('hostQueueTools').classList.remove('visible');
    $('guestNote').style.display = 'block';
    toast(t('toast_host_exited'), '👋');
    await refresh();
    render();
  } catch {
    toast(t('toast_host_exit_failed'), '⚠');
  }
}

function openProfile() {
  $('guestNameInput').value = guestName;
  $('profileModal').classList.add('active');
  setTimeout(() => $('guestNameInput').focus(), 50);
}

function closeProfile() {
  $('profileModal').classList.remove('active');
}

function openPinFromProfile() {
  closeProfile();
  openPin();
}

function saveGuestName() {
  guestName = ($('guestNameInput').value || '').trim().slice(0, 32);
  if (guestName) {
    localStorage.setItem('guest_name', guestName);
  } else {
    localStorage.removeItem('guest_name');
  }
  $('roleText').textContent = state?.aliasName || guestName || state?.fallbackName || t('header_guest');
  closeProfile();
  render();
  toast(guestName ? t('toast_name_saved', { name: guestName }) : t('toast_name_cleared'), '✎');
  api('/api/name', {
    method: 'POST',
    body: JSON.stringify({ name: guestName })
  }).then(() => refresh()).catch(() => {});
}

function openPin() {
  pinMode = 'host';
  $('pinModal').querySelector('.pin-title').textContent = t('pin_title');
  $('pinModal').querySelector('.pin-sub').textContent = t('pin_sub');
  pin = '';
  updateDots();
  $('pinModal').classList.add('active');
}

function closePin() {
  $('pinModal').classList.remove('active');
  pin = '';
}

function pinPress(value) {
  if (pin.length >= 4) return;
  pin += value;
  updateDots();
  if (pin.length === 4) setTimeout(verifyPin, 120);
}

function pinBack() {
  pin = pin.slice(0, -1);
  updateDots();
}

function updateDots() {
  for (let i = 0; i < 4; i++) $('d' + i).classList.toggle('full', i < pin.length);
}

async function verifyPin() {
  try {
    const result = await api('/api/auth', { method: 'POST', body: JSON.stringify({ pin }) });
    if (result.ok) {
      closePin();
      canControl = true;
      canAdmin = result.role === 'admin';
      toast(t(canAdmin ? 'toast_admin_on' : 'toast_host_on'), canAdmin ? '🛠' : '👑');
      await refresh();
    } else {
      toast(t('toast_pin_invalid'), '❌');
      pin = '';
      updateDots();
    }
  } catch (error) {
    toast(t(error && error.message === '401' ? 'toast_pin_invalid' : 'toast_pin_failed'), '❌');
    pin = '';
    updateDots();
  }
}

function searchChanged() {
  $('clearSearch').classList.toggle('visible', $('search').value.length > 0);
  clearTimeout(searchTimer);
  searchTimer = setTimeout(render, 250);
  clearTimeout(suggestTimer);
  suggestTimer = setTimeout(loadSuggestions, 140);
}

async function loadSuggestions() {
  const input = $('search');
  const box = $('searchSuggest');
  if (!input || !box) return;
  const query = input.value.trim();
  if (query.length < 2) {
    box.hidden = true;
    box.innerHTML = '';
    return;
  }

  try {
    const suggestions = await api('/api/suggest?q=' + encodeURIComponent(query));
    box.innerHTML = '';
    suggestions.slice(0, 6).forEach(value => {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = value;
      button.addEventListener('click', () => {
        input.value = value;
        $('clearSearch').classList.add('visible');
        box.hidden = true;
        box.innerHTML = '';
        render();
      });
      box.append(button);
    });
    box.hidden = suggestions.length === 0;
  } catch {
    box.hidden = true;
  }
}

function clearSearch() {
  $('search').value = '';
  $('clearSearch').classList.remove('visible');
  render();
}

async function refresh() {
  try {
    apply(await api('/api/state'));
  } catch {
    // Keep the current state; the page may be on a stale token.
  }
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectSocket();
  }, reconnectDelay);
  reconnectDelay = Math.min(reconnectDelay * 2, 10000);
}

function connectSocket() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  let ws;
  try {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    ws = new WebSocket(proto + '//' + location.host + '/ws?token=' + encodeURIComponent(token));
  } catch {
    scheduleReconnect();
    return;
  }

  socket = ws;
  ws.onopen = () => {
    reconnectDelay = 1000;
    refresh();
  };
  ws.onmessage = event => {
    try {
      apply(JSON.parse(event.data));
    } catch {
      // Ignore a malformed frame; polling will recover the state.
    }
  };
  ws.onerror = () => {
    try { ws.close(); } catch { /* already closed */ }
  };
  ws.onclose = () => {
    if (socket === ws) socket = null;
    refresh();
    scheduleReconnect();
  };
}

// После сна телефона/переключения сети сразу подтягиваем состояние
// и переподключаем WebSocket, не дожидаясь следующего тика таймера.
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) {
    refresh();
    connectSocket();
  }
});
window.addEventListener('pageshow', () => {
  refresh();
  connectSocket();
});
window.addEventListener('online', () => {
  refresh();
  connectSocket();
});

document.addEventListener('pointerdown', event => {
  const dropdown = $('userProfileDropdown');
  if (dropdown && !dropdown.contains(event.target)) closeProfileMenu();
}, true);

document.addEventListener('keydown', event => {
  if (event.key === 'Escape') {
    closeProfileMenu();
    if ($('requestsModal')?.classList.contains('active')) closeRequests();
  }
});

function reportClientError(message) {
  try {
    fetch('/api/client-error?token=' + encodeURIComponent(token), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: String(message || 'unknown').slice(0, 500) })
    }).catch(() => {});
  } catch {
    // Diagnostics must never break the remote.
  }
}

window.addEventListener('error', event => reportClientError(event.message));
window.addEventListener('unhandledrejection', event => {
  reportClientError(event.reason && event.reason.message ? event.reason.message : event.reason);
});

(async function start() {
  await loadLocaleManifest();
  await loadLanguage(language, false);
  await refresh();
  connectSocket();
  setInterval(refresh, 3000);
})();
</script>
</body>
</html>
""";
}
