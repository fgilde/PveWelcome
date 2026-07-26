using PveWelcome.Models;

namespace PveWelcome.Services;

public record LandingTemplate(string Key, string Name, string Description, string Html, string Css, string Js);

/// Built-in starting points for landing pages. Each is fully self-contained (its own CSS/JS).
public static class LandingTemplates
{
    public static readonly List<LandingTemplate> All =
    [
        new("classic", "Classic (nksoft)", "Das originale nksoft-Design: Hero mit schwebenden Orbs + gilde-Logo.",
            """
            <div class="landing">
              <div class="bg-orbs"><span class="orb o1"></span><span class="orb o2"></span><span class="orb o3"></span></div>
              <div class="landing-inner">
                <a class="gilde-logo-link" href="https://www.gilde.org" target="_blank" rel="noopener"><img class="gilde-logo" src="/gilde-logo.png" alt="gilde.org"></a>
                <div class="badge">self-hosted · powered by Proxmox</div>
                <h1 class="hero-title">nksoft</h1>
                <p class="hero-tagline">Software &amp; Infrastruktur.</p>
                <div class="cta-row">
                  <a class="btn btn-primary" href="https://www.gilde.org" target="_blank" rel="noopener">gilde.org ↗</a>
                  <a class="btn btn-ghost" href="/login">Login</a>
                </div>
                <p class="hero-foot">Custom Software · Open Source · Hosting</p>
              </div>
            </div>
            """,
            """
            .landing{position:relative;min-height:100vh;overflow:hidden;display:grid;place-items:center;text-align:center;font-family:Inter,system-ui,sans-serif;color:#e7eaf0;
              background:radial-gradient(1200px 600px at 50% -10%,color-mix(in srgb,#8b5cf6 22%,transparent),transparent 60%),#0b0d12}
            .landing .bg-orbs{position:absolute;inset:0;filter:blur(80px);opacity:.55}
            .landing .orb{position:absolute;border-radius:50%}
            .landing .o1{width:380px;height:380px;background:#8b5cf6;top:8%;left:12%;animation:float 14s ease-in-out infinite}
            .landing .o2{width:300px;height:300px;background:#06b6d4;bottom:6%;right:14%;animation:float 18s ease-in-out infinite reverse}
            .landing .o3{width:260px;height:260px;background:#d946ef;top:40%;right:40%;animation:float 22s ease-in-out infinite}
            @keyframes float{0%,100%{transform:translateY(0)}50%{transform:translateY(-40px)}}
            .landing .landing-inner{position:relative;z-index:1;padding:2rem}
            .landing .gilde-logo-link{display:block;margin:0 auto 1rem;width:fit-content;transition:.25s}
            .landing .gilde-logo-link:hover{transform:translateY(-3px) scale(1.02)}
            .landing .gilde-logo{display:block;margin:0 auto;width:300px;height:300px;object-fit:contain;
              -webkit-mask-image:radial-gradient(ellipse 62% 62% at 50% 48%,#000 56%,transparent 82%);mask-image:radial-gradient(ellipse 62% 62% at 50% 48%,#000 56%,transparent 82%);
              filter:drop-shadow(0 0 34px color-mix(in srgb,#8b5cf6 55%,transparent))}
            .landing .badge{display:inline-block;font-size:.8rem;letter-spacing:.05em;text-transform:uppercase;color:#8b93a7;border:1px solid #232838;border-radius:999px;padding:.35rem .9rem;margin-bottom:1.5rem}
            .landing .hero-title{font-size:clamp(3rem,12vw,7rem);font-weight:800;margin:0;line-height:1;background:linear-gradient(180deg,#fff,color-mix(in srgb,#8b5cf6 60%,#fff));-webkit-background-clip:text;background-clip:text;color:transparent}
            .landing .hero-tagline{font-size:1.25rem;color:#8b93a7;margin:1rem 0 2rem}
            .landing .cta-row{display:flex;gap:1rem;justify-content:center;flex-wrap:wrap}
            .landing .btn{display:inline-flex;align-items:center;gap:.4rem;border:1px solid #232838;background:#171b25;color:#e7eaf0;padding:.8rem 1.6rem;border-radius:10px;font-weight:600;font-size:1rem;cursor:pointer;text-decoration:none;transition:.15s}
            .landing .btn:hover{border-color:#8b5cf6;transform:translateY(-1px)}
            .landing .btn-primary{background:#8b5cf6;border-color:#8b5cf6;color:#fff}
            .landing .btn-ghost{background:transparent}
            .landing .hero-foot{margin-top:2.5rem;font-size:.85rem;color:#8b93a7}
            """,
            ""),

        new("aurora", "Aurora", "Animierter Farbverlauf-Hintergrund, groß und modern.",
            """
            <div class="lp-aurora">
              <div class="content">
                <h1>Willkommen</h1>
                <p>Deine Beschreibung hier. Bearbeite HTML/CSS/JS frei.</p>
                <a class="btn" href="#">Los geht's</a>
              </div>
            </div>
            """,
            """
            .lp-aurora{position:fixed;inset:0;display:grid;place-items:center;color:#fff;font-family:Inter,system-ui,sans-serif;text-align:center;
              background:linear-gradient(-45deg,#ee7752,#e73c7e,#23a6d5,#23d5ab);background-size:400% 400%;animation:grad 15s ease infinite}
            @keyframes grad{0%{background-position:0 50%}50%{background-position:100% 50%}100%{background-position:0 50%}}
            .lp-aurora h1{font-size:clamp(2.5rem,9vw,5.5rem);margin:0;font-weight:800;text-shadow:0 4px 30px rgba(0,0,0,.25)}
            .lp-aurora p{font-size:1.15rem;opacity:.95;margin:.8rem 0 1.6rem}
            .lp-aurora .btn{display:inline-block;padding:.8rem 1.6rem;border-radius:999px;background:#fff;color:#111;font-weight:700;text-decoration:none}
            """,
            ""),

        new("video", "Video-Hintergrund", "Vollflächiges Hintergrundvideo mit Overlay-Hero. URL im HTML anpassen.",
            """
            <div class="lp-video">
              <video autoplay muted loop playsinline poster="">
                <source src="https://cdn.coverr.co/videos/coverr-a-city-at-night-1573/1080p.mp4" type="video/mp4">
              </video>
              <div class="overlay"></div>
              <div class="hero">
                <h1>Dein Titel</h1>
                <p>Untertitel — ersetze das Video über die src-URL im HTML.</p>
              </div>
            </div>
            """,
            """
            .lp-video{position:fixed;inset:0;overflow:hidden;color:#fff;font-family:Inter,system-ui,sans-serif}
            .lp-video video{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
            .lp-video .overlay{position:absolute;inset:0;background:linear-gradient(180deg,#0007,#000a)}
            .lp-video .hero{position:relative;z-index:2;height:100%;display:grid;place-items:center;text-align:center;padding:2rem}
            .lp-video h1{font-size:clamp(3rem,10vw,6rem);margin:0;font-weight:800}
            .lp-video p{font-size:1.2rem;opacity:.9;margin-top:.6rem}
            """,
            ""),

        new("terminal", "Terminal", "Dunkler Monospace-Look mit Tipp-Animation.",
            """
            <div class="lp-term">
              <pre id="t"></pre>
            </div>
            """,
            """
            .lp-term{position:fixed;inset:0;background:#0a0e12;color:#3ee66b;font-family:'JetBrains Mono',ui-monospace,monospace;display:grid;place-items:center}
            .lp-term pre{font-size:clamp(1rem,3vw,1.6rem);white-space:pre-wrap;max-width:80ch;padding:2rem}
            .lp-term pre::after{content:'▋';animation:blink 1s steps(1) infinite}
            @keyframes blink{50%{opacity:0}}
            """,
            """
            const lines=["> booting nksoft...","> systems online","> welcome."];
            const el=document.getElementById('t');let i=0,j=0;
            (function type(){if(i<lines.length){if(j<=lines[i].length){el.textContent=lines.slice(0,i).join('\n')+(i?'\n':'')+lines[i].slice(0,j);j++;setTimeout(type,45)}else{i++;j=0;setTimeout(type,500)}}})();
            """),

        new("links", "Link-Grid", "Homepage-Stil: Kachel-Grid mit Service-Links.",
            """
            <div class="lp-links">
              <h1>Services</h1>
              <div class="grid">
                <a href="#"><b>App</b><span>beschreibung</span></a>
                <a href="#"><b>Dienst</b><span>beschreibung</span></a>
                <a href="#"><b>Tool</b><span>beschreibung</span></a>
                <a href="#"><b>Mehr</b><span>beschreibung</span></a>
              </div>
            </div>
            """,
            """
            .lp-links{min-height:100vh;background:#0f1117;color:#e6e6e6;font-family:Inter,system-ui,sans-serif;padding:4rem 6vw}
            .lp-links h1{font-weight:800;font-size:2rem;margin:0 0 1.6rem}
            .lp-links .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:1rem}
            .lp-links a{display:flex;flex-direction:column;gap:.3rem;padding:1.2rem;border:1px solid #ffffff14;border-radius:14px;background:#161a23;text-decoration:none;color:inherit;transition:.15s}
            .lp-links a:hover{border-color:#6366f1;transform:translateY(-2px)}
            .lp-links b{font-size:1.1rem}
            .lp-links span{color:#9aa0ab;font-size:.85rem}
            """,
            ""),

        new("cyber", "Cyber-Lab", "Dunkles Hacker-Lab: Neon-Grid, Glitch-Titel, Scanlines.",
            """
            <div class="lp-cyber">
              <div class="grid"></div><div class="scan"></div>
              <div class="mid">
                <div class="tagline">// build · break · ship</div>
                <h1 data-t="nksoft">nksoft</h1>
                <p class="sub">Software &amp; Infrastruktur — self-hosted.</p>
                <a class="btn" href="#">ENTER ▸</a>
              </div>
            </div>
            """,
            """
            .lp-cyber{position:fixed;inset:0;overflow:hidden;background:#05070a;color:#c8ffe6;font-family:'JetBrains Mono',ui-monospace,monospace;display:grid;place-items:center;text-align:center}
            .lp-cyber .grid{position:absolute;inset:-2px;background-image:linear-gradient(#0aff9d22 1px,transparent 1px),linear-gradient(90deg,#0aff9d22 1px,transparent 1px);background-size:44px 44px;
              transform:perspective(500px) rotateX(60deg) translateY(-10%);transform-origin:top;animation:slide 6s linear infinite;opacity:.6}
            @keyframes slide{to{background-position:0 44px}}
            .lp-cyber .scan{position:absolute;inset:0;background:repeating-linear-gradient(#0000 0 2px,#0aff9d0a 2px 4px);pointer-events:none}
            .lp-cyber .mid{position:relative;z-index:2;padding:2rem}
            .lp-cyber .tagline{color:#57d1ff;letter-spacing:.3em;font-size:.8rem;margin-bottom:1rem}
            .lp-cyber h1{position:relative;font-size:clamp(3rem,13vw,8rem);margin:0;font-weight:800;color:#eafff6;text-shadow:0 0 18px #0aff9d88}
            .lp-cyber h1::before,.lp-cyber h1::after{content:attr(data-t);position:absolute;left:0;top:0;width:100%}
            .lp-cyber h1::before{color:#ff2e88;animation:g1 2.5s infinite steps(2)}
            .lp-cyber h1::after{color:#22d3ee;animation:g2 3s infinite steps(2)}
            @keyframes g1{0%,97%{clip-path:inset(0 0 100% 0);transform:translate(0)}50%{clip-path:inset(20% 0 40% 0);transform:translate(-3px,1px)}}
            @keyframes g2{0%,94%{clip-path:inset(100% 0 0 0);transform:translate(0)}55%{clip-path:inset(60% 0 10% 0);transform:translate(3px,-1px)}}
            .lp-cyber .sub{color:#7fdcc0;margin:1rem 0 1.8rem}
            .lp-cyber .btn{display:inline-block;padding:.7rem 1.6rem;border:1px solid #0aff9d;color:#0aff9d;text-decoration:none;font-weight:700;letter-spacing:.15em;border-radius:4px;transition:.15s}
            .lp-cyber .btn:hover{background:#0aff9d;color:#04120c;box-shadow:0 0 24px #0aff9daa}
            """,
            ""),

        new("spotlight", "Spotlight", "Ein Lichtkegel folgt der Maus über dunklem Grund.",
            """
            <div class="lp-spot" id="spot">
              <div class="c"><h1>nksoft</h1><p>Beweg die Maus.</p></div>
            </div>
            """,
            """
            .lp-spot{position:fixed;inset:0;background:#0a0a0f radial-gradient(300px circle at var(--x,50%) var(--y,50%),#ffffff14,transparent 60%);
              color:#f0f0f5;font-family:Inter,system-ui,sans-serif;display:grid;place-items:center;text-align:center;transition:background .05s}
            .lp-spot h1{font-size:clamp(3rem,11vw,7rem);margin:0;font-weight:800}
            .lp-spot p{color:#9aa;margin-top:.5rem}
            """,
            """
            const s=document.getElementById('spot');
            addEventListener('mousemove',e=>{s.style.setProperty('--x',e.clientX+'px');s.style.setProperty('--y',e.clientY+'px');});
            """),
    ];

    public static LandingTemplate Get(string key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];

    public static LandingPage NewFrom(string key)
    {
        var t = Get(key);
        return new LandingPage { Name = t.Name, Template = t.Key, Html = t.Html, Css = t.Css, Js = t.Js };
    }
}
