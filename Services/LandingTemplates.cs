using PveWelcome.Models;

namespace PveWelcome.Services;

public record LandingTemplate(string Key, string Name, string Description, string Html, string Css, string Js);

/// Built-in starting points for landing pages. Each is fully self-contained (its own CSS/JS).
public static class LandingTemplates
{
    public static readonly List<LandingTemplate> All =
    [
        new("classic", "Classic (nksoft)", "Ruhiger Hero mit schwebenden Orbs — das aktuelle Design.",
            """
            <div class="lp-classic">
              <span class="orb o1"></span><span class="orb o2"></span><span class="orb o3"></span>
              <div class="inner">
                <div class="badge">self-hosted · powered by Proxmox</div>
                <h1>nksoft</h1>
                <p class="tag">Custom Software · Open Source · Hosting</p>
                <a class="cta" href="https://gilde.org" target="_blank" rel="noopener">gilde.org ↗</a>
              </div>
            </div>
            """,
            """
            .lp-classic{position:fixed;inset:0;display:grid;place-items:center;overflow:hidden;
              background:radial-gradient(1200px 800px at 50% -10%,#241b45,#0b0912 60%);color:#eee;font-family:Inter,system-ui,sans-serif;text-align:center}
            .lp-classic .inner{position:relative;z-index:2;padding:2rem}
            .lp-classic .badge{display:inline-block;padding:.35rem .8rem;border:1px solid #ffffff22;border-radius:999px;font-size:.72rem;letter-spacing:.14em;text-transform:uppercase;color:#b6aede;margin-bottom:1.4rem}
            .lp-classic h1{font-size:clamp(3rem,10vw,6rem);font-weight:800;margin:0;background:linear-gradient(90deg,#a78bfa,#8b5cf6);-webkit-background-clip:text;background-clip:text;color:transparent}
            .lp-classic .tag{color:#c9c4e0;font-size:1.05rem;margin:.6rem 0 1.8rem}
            .lp-classic .cta{display:inline-block;padding:.7rem 1.4rem;border-radius:10px;background:#8b5cf6;color:#fff;text-decoration:none;font-weight:700}
            .lp-classic .orb{position:absolute;border-radius:50%;filter:blur(60px);opacity:.5;animation:float 14s ease-in-out infinite}
            .lp-classic .o1{width:340px;height:340px;background:#7c3aed;top:-60px;left:-40px}
            .lp-classic .o2{width:280px;height:280px;background:#2563eb;bottom:-60px;right:-30px;animation-delay:-4s}
            .lp-classic .o3{width:220px;height:220px;background:#db2777;bottom:20%;left:20%;animation-delay:-8s}
            @keyframes float{0%,100%{transform:translateY(0)}50%{transform:translateY(-30px)}}
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
    ];

    public static LandingTemplate Get(string key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];

    public static LandingPage NewFrom(string key)
    {
        var t = Get(key);
        return new LandingPage { Name = t.Name, Template = t.Key, Html = t.Html, Css = t.Css, Js = t.Js };
    }
}
