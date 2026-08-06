using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RasHub.Web.Authentication;

namespace RasHub.Web.Endpoints;

internal static class ApiDocumentationAuthenticationEndpoints
{
    private const string PageStyles = """
                                      :root {
                                          color-scheme: dark;
                                          --color-1: #fff;
                                          --color-2: rgba(255, 255, 255, .62);
                                          --color-3: rgba(255, 255, 255, .42);
                                          --background-1: #000;
                                          --background-2: #080808;
                                          --border-color: rgba(255, 255, 255, .16);
                                          --focus: #fff;
                                          --error: #fff;
                                          font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                                          background: var(--background-1);
                                          color: var(--color-1);
                                      }

                                      * { box-sizing: border-box; }

                                      body {
                                          min-height: 100vh;
                                          margin: 0;
                                          display: grid;
                                          place-items: center;
                                          padding: 24px;
                                          position: relative;
                                          isolation: isolate;
                                          overflow: hidden;
                                          background: var(--background-1);
                                      }

                                      body::before,
                                      body::after {
                                          content: "";
                                          position: fixed;
                                          pointer-events: none;
                                      }

                                      body::before {
                                          inset: 0;
                                          z-index: -2;
                                          opacity: .38;
                                          background-image:
                                              radial-gradient(1px 1px at 18px 28px, rgba(255, 255, 255, .55), transparent),
                                              radial-gradient(1px 1px at 82px 74px, rgba(255, 255, 255, .28), transparent),
                                              radial-gradient(1.5px 1.5px at 148px 38px, rgba(255, 255, 255, .45), transparent),
                                              radial-gradient(1px 1px at 198px 126px, rgba(255, 255, 255, .24), transparent);
                                          background-size: 240px 170px;
                                          background-position: 0 0, 30px 14px, 70px 42px, 110px 6px;
                                          animation: star-drift 70s linear infinite;
                                      }

                                      body::after {
                                          inset: -15% -8% 0;
                                          z-index: -1;
                                          opacity: .72;
                                          filter: blur(24px);
                                          background:
                                              linear-gradient(104deg, transparent 28%, rgba(255, 255, 255, .045) 39%, transparent 51%),
                                              linear-gradient(76deg, transparent 42%, rgba(255, 255, 255, .035) 50%, transparent 60%),
                                              radial-gradient(ellipse at 50% -8%, rgba(255, 255, 255, .12), transparent 58%);
                                          animation: light-shift 12s ease-in-out infinite alternate;
                                      }

                                      main {
                                          width: min(100%, 980px);
                                          min-height: 520px;
                                          padding: 0;
                                          position: relative;
                                          display: grid;
                                          grid-template-columns: 1.15fr .85fr;
                                          overflow: hidden;
                                          border: 1px solid var(--border-color);
                                          border-radius: 0;
                                          background: rgba(0, 0, 0, .74);
                                          box-shadow:
                                              0 32px 100px rgba(0, 0, 0, .7),
                                              inset 0 1px rgba(255, 255, 255, .035);
                                          backdrop-filter: blur(14px);
                                          animation:
                                              portal-reveal .9s cubic-bezier(.2, .8, .2, 1) both,
                                              panel-breathe 7s ease-in-out .9s infinite;
                                      }

                                      main::before {
                                          content: "";
                                          position: absolute;
                                          top: -1px;
                                          left: -42%;
                                          width: 42%;
                                          height: 1px;
                                          background: linear-gradient(90deg, transparent, #fff, transparent);
                                          opacity: .8;
                                          animation: edge-sweep 5.5s ease-in-out infinite;
                                      }

                                      main > * {
                                          position: relative;
                                          z-index: 1;
                                      }

                                      .portal-compact {
                                          width: min(100%, 410px);
                                          min-height: 0;
                                          display: block;
                                      }

                                      .terminal {
                                          min-width: 0;
                                          padding: 42px 38px 30px;
                                          display: flex;
                                          flex-direction: column;
                                          justify-content: center;
                                          animation: terminal-reveal .8s .42s cubic-bezier(.2, .8, .2, 1) both;
                                      }

                                      .portal-compact .terminal { padding: 28px; }

                                      .eyebrow {
                                          display: flex;
                                          align-items: flex-start;
                                          justify-content: space-between;
                                          gap: 12px;
                                          margin-bottom: 72px;
                                          color: var(--color-3);
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                          font-size: 10px;
                                          letter-spacing: .08em;
                                      }

                                      .portal-compact .eyebrow { margin-bottom: 44px; }

                                      .identity {
                                          display: flex;
                                          flex-direction: column;
                                          gap: 5px;
                                      }

                                      .environment { color: var(--color-2); }

                                      .status {
                                          padding: 4px 7px;
                                          border: 1px solid var(--border-color);
                                          border-radius: 0;
                                          color: var(--color-2);
                                          letter-spacing: .05em;
                                      }

                                      .signal {
                                          min-height: 520px;
                                          position: relative;
                                          overflow: hidden;
                                          border-right: 1px solid var(--border-color);
                                          background:
                                              radial-gradient(circle at 50% 50%, rgba(255, 255, 255, .075), transparent 18%),
                                              radial-gradient(circle at 50% 50%, rgba(255, 255, 255, .025), transparent 55%);
                                          animation: signal-reveal 1.15s .12s cubic-bezier(.16, 1, .3, 1) both;
                                      }

                                      .signal-identity {
                                          position: absolute;
                                          top: 42px;
                                          left: 38px;
                                          z-index: 2;
                                          color: var(--color-3);
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                          font-size: 10px;
                                          letter-spacing: .08em;
                                      }

                                      .signal-grid {
                                          position: absolute;
                                          inset: 0;
                                          opacity: .18;
                                          background-image:
                                              linear-gradient(rgba(255, 255, 255, .11) 1px, transparent 1px),
                                              linear-gradient(90deg, rgba(255, 255, 255, .11) 1px, transparent 1px);
                                          background-size: 32px 32px;
                                          mask-image: radial-gradient(circle at center, #000, transparent 72%);
                                      }

                                      .axis {
                                          position: absolute;
                                          top: 50%;
                                          left: 50%;
                                          background: linear-gradient(90deg, transparent, rgba(255, 255, 255, .2), transparent);
                                          transform: translate(-50%, -50%);
                                      }

                                      .axis-horizontal { width: 78%; height: 1px; }
                                      .axis-vertical {
                                          width: 1px;
                                          height: 78%;
                                          background: linear-gradient(transparent, rgba(255, 255, 255, .2), transparent);
                                      }

                                      .orbit {
                                          position: absolute;
                                          top: 50%;
                                          left: 50%;
                                          border: 1px solid rgba(255, 255, 255, .24);
                                          border-radius: 50%;
                                          transform: translate(-50%, -50%);
                                      }

                                      .orbit span {
                                          position: absolute;
                                          top: -3px;
                                          left: 50%;
                                          width: 6px;
                                          height: 6px;
                                          border-radius: 50%;
                                          background: #fff;
                                          box-shadow: 0 0 18px rgba(255, 255, 255, .72);
                                      }

                                      .orbit-one {
                                          width: 286px;
                                          height: 286px;
                                          animation: orbit-one 20s linear infinite;
                                      }

                                      .orbit-two {
                                          width: 330px;
                                          height: 148px;
                                          animation: orbit-two 13s linear infinite reverse;
                                      }

                                      .orbit-three {
                                          width: 176px;
                                          height: 352px;
                                          opacity: .65;
                                          animation: orbit-three 28s linear infinite;
                                      }

                                      .scan {
                                          width: 174px;
                                          height: 1px;
                                          position: absolute;
                                          top: 50%;
                                          left: 50%;
                                          transform-origin: left center;
                                          background: linear-gradient(90deg, rgba(255, 255, 255, .7), transparent);
                                          animation: scan 8s linear infinite;
                                      }

                                      .core {
                                          width: 78px;
                                          height: 78px;
                                          position: absolute;
                                          top: 50%;
                                          left: 50%;
                                          display: grid;
                                          place-items: center;
                                          border: 1px solid rgba(255, 255, 255, .55);
                                          border-radius: 50%;
                                          background: #000;
                                          color: #fff;
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                          font-size: 10px;
                                          letter-spacing: .08em;
                                          transform: translate(-50%, -50%);
                                          box-shadow:
                                              0 0 0 12px rgba(255, 255, 255, .025),
                                              0 0 0 28px rgba(255, 255, 255, .018),
                                              0 0 70px rgba(255, 255, 255, .14);
                                          animation: core-pulse 4s ease-in-out infinite;
                                      }

                                      header {
                                          display: flex;
                                          align-items: center;
                                          gap: 8px;
                                          margin-bottom: 8px;
                                      }

                                      h1 {
                                          margin: 0;
                                          font-size: 18px;
                                          font-weight: 600;
                                          letter-spacing: -.01em;
                                      }

                                      .prompt {
                                          color: var(--color-2);
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                      }

                                      .cursor {
                                          width: 7px;
                                          height: 17px;
                                          background: var(--color-1);
                                          box-shadow: 0 0 14px rgba(255, 255, 255, .38);
                                          animation:
                                              blink 1.1s steps(1) infinite,
                                              cursor-pulse 2.2s ease-in-out infinite;
                                      }

                                      .context {
                                          margin: 0;
                                          color: var(--color-2);
                                          font-size: 12px;
                                          line-height: 1.6;
                                      }

                                      .login { margin-top: 26px; }

                                      input {
                                          width: 100%;
                                          height: 41px;
                                          margin-bottom: 14px;
                                          padding: 0 11px;
                                          border: 1px solid var(--border-color);
                                          border-radius: 0;
                                          outline: none;
                                          background: rgba(0, 0, 0, .72);
                                          color: var(--color-1);
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                          font-size: 13px;
                                          caret-color: var(--focus);
                                      }

                                      input::placeholder {
                                          color: rgba(255, 255, 255, .28);
                                          opacity: 1;
                                      }

                                      input:focus {
                                          border-color: var(--focus);
                                          box-shadow:
                                              inset 0 0 0 1px var(--focus),
                                              0 0 28px rgba(255, 255, 255, .06);
                                      }

                                      button, a {
                                          border: 0;
                                          border-radius: 0;
                                          background: var(--color-1);
                                          color: var(--background-1);
                                          font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                          font-size: 12px;
                                          text-decoration: none;
                                          cursor: pointer;
                                          transition: opacity .15s;
                                      }

                                      button:hover, button:focus-visible,
                                      a:hover, a:focus-visible {
                                          color: var(--background-1);
                                          opacity: .82;
                                          outline: none;
                                      }

                                      .login button {
                                          width: 100%;
                                          min-height: 41px;
                                          margin: 4px 0 0;
                                          padding: 9px 12px;
                                          border: 1px solid var(--border-color);
                                          background: var(--background-2);
                                          color: var(--color-1);
                                          opacity: 1;
                                      }

                                      .login button:hover,
                                      .login button:focus-visible {
                                          border-color: var(--color-1);
                                          background: var(--color-1);
                                          color: var(--background-1);
                                          opacity: 1;
                                      }

                                      .error {
                                          margin: 20px 0 -10px;
                                          padding-left: 10px;
                                          border-left: 1px solid var(--color-1);
                                          color: var(--error);
                                          font-size: 11px;
                                      }

                                      .message {
                                          margin: 26px 0 20px;
                                          color: var(--color-3);
                                          font-size: 12px;
                                      }

                                      .actions {
                                          display: flex;
                                          gap: 20px;
                                      }

                                      .actions button,
                                      .actions a {
                                          display: inline-grid;
                                          min-height: 34px;
                                          place-items: center;
                                          padding: 7px 12px;
                                      }

                                      .actions a {
                                          border: 1px solid var(--border-color);
                                          background: var(--background-2);
                                          color: var(--color-1);
                                      }

                                      .actions a:hover,
                                      .actions a:focus-visible { color: var(--color-1); }

                                      @keyframes blink { 50% { opacity: .08; } }

                                      @keyframes cursor-pulse {
                                          0%, 100% { box-shadow: 0 0 8px rgba(255, 255, 255, .18); }
                                          50% { box-shadow: 0 0 22px rgba(255, 255, 255, .58); }
                                      }

                                      @keyframes star-drift {
                                          to {
                                              background-position:
                                                  240px 170px,
                                                  270px 184px,
                                                  310px 212px,
                                                  350px 176px;
                                          }
                                      }

                                      @keyframes light-shift {
                                          0% { transform: translate3d(-2%, -1%, 0) scale(1); opacity: .58; }
                                          100% { transform: translate3d(2%, 2%, 0) scale(1.08); opacity: .82; }
                                      }

                                      @keyframes edge-sweep {
                                          0%, 16% { transform: translateX(0); opacity: 0; }
                                          24% { opacity: .8; }
                                          62% { opacity: .8; }
                                          72%, 100% { transform: translateX(338%); opacity: 0; }
                                      }

                                      @keyframes panel-breathe {
                                          0%, 100% { border-color: rgba(255, 255, 255, .13); }
                                          50% { border-color: rgba(255, 255, 255, .22); }
                                      }

                                      @keyframes portal-reveal {
                                          from {
                                              opacity: 0;
                                              clip-path: inset(49% 0 49% 0);
                                              transform: scale(.985);
                                          }
                                          to {
                                              opacity: 1;
                                              clip-path: inset(0 0 0 0);
                                              transform: scale(1);
                                          }
                                      }

                                      @keyframes signal-reveal {
                                          from { opacity: 0; transform: scale(.78) rotate(-3deg); }
                                          to { opacity: 1; transform: scale(1) rotate(0); }
                                      }

                                      @keyframes terminal-reveal {
                                          from { opacity: 0; transform: translateX(22px); }
                                          to { opacity: 1; transform: translateX(0); }
                                      }

                                      @keyframes orbit-one {
                                          from { transform: translate(-50%, -50%) rotate(0); }
                                          to { transform: translate(-50%, -50%) rotate(360deg); }
                                      }

                                      @keyframes orbit-two {
                                          from { transform: translate(-50%, -50%) rotate(58deg); }
                                          to { transform: translate(-50%, -50%) rotate(418deg); }
                                      }

                                      @keyframes orbit-three {
                                          from { transform: translate(-50%, -50%) rotate(-24deg); }
                                          to { transform: translate(-50%, -50%) rotate(336deg); }
                                      }

                                      @keyframes scan {
                                          from { transform: rotate(0); opacity: .16; }
                                          50% { opacity: .72; }
                                          to { transform: rotate(360deg); opacity: .16; }
                                      }

                                      @keyframes core-pulse {
                                          0%, 100% {
                                              box-shadow:
                                                  0 0 0 12px rgba(255, 255, 255, .025),
                                                  0 0 0 28px rgba(255, 255, 255, .018),
                                                  0 0 55px rgba(255, 255, 255, .1);
                                          }
                                          50% {
                                              box-shadow:
                                                  0 0 0 16px rgba(255, 255, 255, .035),
                                                  0 0 0 34px rgba(255, 255, 255, .022),
                                                  0 0 95px rgba(255, 255, 255, .24);
                                          }
                                      }

                                      @media (max-width: 820px) {
                                          body { overflow: auto; }

                                          .portal {
                                              width: min(100%, 410px);
                                              min-height: 0;
                                              display: block;
                                          }

                                          .signal { display: none; }
                                          .terminal { min-height: 480px; padding: 30px 28px 24px; }
                                          .portal-compact .terminal { min-height: 0; }
                                          .eyebrow { margin-bottom: 58px; }
                                      }

                                      @media (max-height: 650px) {
                                          body { overflow: auto; }
                                      }

                                      @media (prefers-reduced-motion: reduce) {
                                          body::before,
                                          body::after,
                                          main,
                                          main::before,
                                          .cursor,
                                          .signal,
                                          .terminal,
                                          .orbit,
                                          .scan,
                                          .core { animation: none; }
                                      }
                                      """;

    private const string LoginPageStyles = """
                                           :root {
                                               color-scheme: dark;
                                               font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                                               color: #fff;
                                               background: #000;
                                           }

                                           * { box-sizing: border-box; }

                                           body {
                                               min-height: 100vh;
                                               margin: 0;
                                               overflow: hidden;
                                               background: #000;
                                           }

                                           button,
                                           input { font: inherit; }

                                           .login-page {
                                               position: relative;
                                               isolation: isolate;
                                               display: grid;
                                               place-items: center;
                                               padding: 56px 24px 104px;
                                               background:
                                                   radial-gradient(circle at 50% 37%, rgba(255, 255, 255, .055), transparent 27%),
                                                   radial-gradient(circle at 50% 50%, #090909 0, #030303 48%, #000 78%);
                                           }

                                           .login-page::before {
                                               content: "";
                                               position: fixed;
                                               inset: 0;
                                               z-index: -3;
                                               opacity: .48;
                                               background-image:
                                                   radial-gradient(1px 1px at 17px 31px, rgba(255, 255, 255, .7), transparent),
                                                   radial-gradient(1px 1px at 91px 77px, rgba(255, 255, 255, .35), transparent),
                                                   radial-gradient(1px 1px at 151px 19px, rgba(255, 255, 255, .45), transparent),
                                                   radial-gradient(1px 1px at 213px 126px, rgba(255, 255, 255, .24), transparent),
                                                   radial-gradient(1.5px 1.5px at 266px 54px, rgba(255, 255, 255, .5), transparent);
                                               background-size: 300px 170px;
                                               animation: stars 90s linear infinite;
                                           }

                                           .login-page::after {
                                               content: "";
                                               position: fixed;
                                               inset: 0;
                                               z-index: -2;
                                               pointer-events: none;
                                               background:
                                                   linear-gradient(105deg, transparent 31%, rgba(255, 255, 255, .025) 43%, transparent 56%),
                                                   linear-gradient(74deg, transparent 39%, rgba(255, 255, 255, .018) 51%, transparent 63%);
                                           }

                                           .cosmos {
                                               position: fixed;
                                               inset: 0;
                                               z-index: -1;
                                               overflow: hidden;
                                               pointer-events: none;
                                           }

                                           .world-orbit {
                                               position: absolute;
                                               top: 48%;
                                               left: 50%;
                                               border: 1px solid rgba(255, 255, 255, .055);
                                               border-radius: 50%;
                                               transform: translate(-50%, -50%);
                                           }

                                           .world-orbit::after {
                                               content: "";
                                               position: absolute;
                                               width: 7px;
                                               height: 7px;
                                               top: 18%;
                                               right: 8%;
                                               border-radius: 50%;
                                               background: rgba(255, 255, 255, .48);
                                               box-shadow: 0 0 18px rgba(255, 255, 255, .24);
                                           }

                                           .world-orbit-one {
                                               width: min(92vw, 1380px);
                                               height: min(72vw, 820px);
                                               transform: translate(-50%, -50%) rotate(8deg);
                                           }

                                           .world-orbit-two {
                                               width: min(78vw, 1160px);
                                               height: min(42vw, 610px);
                                               transform: translate(-50%, -50%) rotate(-17deg);
                                           }

                                           .world-orbit-three {
                                               width: min(64vw, 940px);
                                               height: min(88vw, 980px);
                                               transform: translate(-50%, -50%) rotate(63deg);
                                           }

                                           .login-shell {
                                               width: min(100%, 560px);
                                               animation: reveal .8s cubic-bezier(.2, .8, .2, 1) both;
                                           }

                                           .login-content {
                                               display: flex;
                                               flex-direction: column;
                                               align-items: center;
                                               text-align: center;
                                           }

                                           .brand-mark {
                                               width: 154px;
                                               height: 154px;
                                               position: relative;
                                               margin-bottom: 14px;
                                           }

                                           .brand-core {
                                               width: 68px;
                                               height: 68px;
                                               position: absolute;
                                               top: 50%;
                                               left: 50%;
                                               border: 1px solid rgba(255, 255, 255, .8);
                                               border-radius: 50%;
                                               background: #030303;
                                               transform: translate(-50%, -50%);
                                               box-shadow:
                                                   0 0 0 5px rgba(255, 255, 255, .025),
                                                   0 0 22px rgba(255, 255, 255, .19),
                                                   inset 0 0 20px rgba(255, 255, 255, .025);
                                           }

                                           .brand-orbit {
                                               position: absolute;
                                               top: 50%;
                                               left: 50%;
                                               width: 138px;
                                               height: 66px;
                                               border: 1px solid rgba(255, 255, 255, .38);
                                               border-radius: 50%;
                                           }

                                           .brand-orbit i {
                                               width: 5px;
                                               height: 5px;
                                               position: absolute;
                                               top: -3px;
                                               left: 50%;
                                               border-radius: 50%;
                                               background: #fff;
                                               box-shadow: 0 0 12px rgba(255, 255, 255, .7);
                                           }

                                           .brand-orbit-one {
                                               animation: mark-one 16s linear infinite;
                                           }

                                           .brand-orbit-two {
                                               animation: mark-two 21s linear infinite reverse;
                                           }

                                           .brand-orbit-three {
                                               animation: mark-three 25s linear infinite;
                                           }

                                           h1 {
                                               margin: 0;
                                               color: #fff;
                                               font-size: clamp(42px, 5vw, 58px);
                                               font-weight: 500;
                                               line-height: 1;
                                               letter-spacing: -.035em;
                                           }

                                           .subtitle {
                                               margin: 9px 0 0;
                                               color: rgba(255, 255, 255, .68);
                                               font-size: 19px;
                                               font-weight: 300;
                                               letter-spacing: .01em;
                                           }

                                           .motto {
                                               width: 100%;
                                               margin: 33px 0 27px;
                                               display: grid;
                                               grid-template-columns: 1fr auto 1fr;
                                               align-items: center;
                                               gap: 18px;
                                           }

                                           .motto span {
                                               height: 1px;
                                               background: linear-gradient(90deg, transparent, rgba(255, 255, 255, .25));
                                           }

                                           .motto span:last-child {
                                               background: linear-gradient(90deg, rgba(255, 255, 255, .25), transparent);
                                           }

                                           .motto p {
                                               margin: 0;
                                               color: rgba(255, 255, 255, .5);
                                               font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                               font-size: 10px;
                                               letter-spacing: .3em;
                                               white-space: nowrap;
                                           }

                                           .login-card {
                                               width: 100%;
                                               padding: 24px;
                                               border: 1px solid rgba(255, 255, 255, .13);
                                               background: rgba(0, 0, 0, .58);
                                               box-shadow:
                                                   0 30px 80px rgba(0, 0, 0, .58),
                                                   inset 0 1px rgba(255, 255, 255, .025);
                                               backdrop-filter: blur(16px);
                                           }

                                           .field {
                                               height: 58px;
                                               margin-bottom: 14px;
                                               position: relative;
                                               display: flex;
                                               align-items: center;
                                               border: 1px solid rgba(255, 255, 255, .17);
                                               background: rgba(0, 0, 0, .38);
                                               transition: border-color .18s, background .18s;
                                           }

                                           .field:focus-within {
                                               border-color: rgba(255, 255, 255, .65);
                                               background: rgba(255, 255, 255, .018);
                                           }

                                           .field svg {
                                               width: 20px;
                                               height: 20px;
                                               margin-left: 20px;
                                               flex: 0 0 auto;
                                               fill: none;
                                               stroke: rgba(255, 255, 255, .58);
                                               stroke-linecap: round;
                                               stroke-linejoin: round;
                                               stroke-width: 1.5;
                                           }

                                           .field input {
                                               width: 100%;
                                               height: 100%;
                                               min-width: 0;
                                               padding: 0 18px;
                                               border: 0;
                                               outline: 0;
                                               background: transparent;
                                               color: #fff;
                                               font-size: 15px;
                                               caret-color: #fff;
                                           }

                                           .field input::placeholder {
                                               color: rgba(255, 255, 255, .35);
                                               opacity: 1;
                                           }

                                           .login-card button {
                                               width: 100%;
                                               height: 58px;
                                               margin-top: 4px;
                                               padding: 0 22px;
                                               display: flex;
                                               align-items: center;
                                               justify-content: center;
                                               position: relative;
                                               border: 1px solid rgba(255, 255, 255, .68);
                                               outline: 0;
                                               background:
                                                   linear-gradient(100deg, rgba(255, 255, 255, .18), rgba(255, 255, 255, .055) 48%, rgba(255, 255, 255, .16)),
                                                   #080808;
                                               color: #fff;
                                               font-size: 16px;
                                               cursor: pointer;
                                               box-shadow:
                                                   0 0 24px rgba(255, 255, 255, .08),
                                                   inset 0 0 22px rgba(255, 255, 255, .035);
                                               transition: background .18s, color .18s, box-shadow .18s;
                                           }

                                           .login-card button span:last-child {
                                               position: absolute;
                                               right: 22px;
                                               font-size: 23px;
                                               font-weight: 200;
                                           }

                                           .login-card button span:first-child {
                                               position: absolute;
                                               left: 50%;
                                               transform: translateX(-50%);
                                           }

                                           .login-card button:hover,
                                           .login-card button:focus-visible {
                                               background: #fff;
                                               color: #000;
                                               box-shadow: 0 0 34px rgba(255, 255, 255, .18);
                                           }

                                           .error {
                                               width: 100%;
                                               margin: 0 0 12px;
                                               padding: 9px 12px;
                                               border-left: 1px solid #fff;
                                               background: rgba(255, 255, 255, .035);
                                               color: rgba(255, 255, 255, .78);
                                               font-size: 12px;
                                               text-align: left;
                                           }

                                           .system-footer {
                                               min-height: 72px;
                                               position: fixed;
                                               right: 0;
                                               bottom: 0;
                                               left: 0;
                                               z-index: 3;
                                               padding: 0 max(28px, calc((100vw - 1160px) / 2));
                                               display: grid;
                                               grid-template-columns: 1fr auto 1fr;
                                               align-items: center;
                                               border-top: 1px solid rgba(255, 255, 255, .1);
                                               background: rgba(0, 0, 0, .68);
                                               color: rgba(255, 255, 255, .45);
                                               font-size: 12px;
                                               letter-spacing: .02em;
                                               backdrop-filter: blur(14px);
                                           }

                                           .system-footer span:nth-child(2) { text-align: center; }
                                           .system-footer span:last-child { text-align: right; }
                                           .system-footer strong {
                                               margin-right: 10px;
                                               color: rgba(255, 255, 255, .82);
                                               font-family: "JetBrains Mono", "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                                               font-weight: 400;
                                           }

                                           @keyframes stars {
                                               to { background-position: 300px 170px; }
                                           }

                                           @keyframes reveal {
                                               from { opacity: 0; transform: translateY(16px); }
                                               to { opacity: 1; transform: translateY(0); }
                                           }

                                           @keyframes mark-one {
                                               from { transform: translate(-50%, -50%) rotate(12deg); }
                                               to { transform: translate(-50%, -50%) rotate(372deg); }
                                           }

                                           @keyframes mark-two {
                                               from { transform: translate(-50%, -50%) rotate(72deg); }
                                               to { transform: translate(-50%, -50%) rotate(432deg); }
                                           }

                                           @keyframes mark-three {
                                               from { transform: translate(-50%, -50%) rotate(132deg); }
                                               to { transform: translate(-50%, -50%) rotate(492deg); }
                                           }

                                           @media (max-height: 820px) {
                                               .login-page { overflow: auto; padding-top: 30px; }
                                               .brand-mark { width: 125px; height: 125px; margin-bottom: 8px; }
                                               h1 { font-size: 42px; }
                                               .subtitle { font-size: 16px; }
                                               .motto { margin: 22px 0 18px; }
                                               .login-card { padding: 19px; }
                                               .field, .login-card button { height: 51px; }
                                           }

                                           @media (max-width: 620px) {
                                               .login-page { overflow: auto; padding: 30px 18px 96px; }
                                               .login-shell { width: min(100%, 440px); }
                                               .motto { gap: 10px; }
                                               .motto p { font-size: 9px; letter-spacing: .18em; }
                                               .login-card { padding: 16px; }
                                               .system-footer {
                                                   min-height: 62px;
                                                   padding: 0 18px;
                                                   grid-template-columns: 1fr 1fr;
                                               }
                                               .system-footer span:nth-child(2) { display: none; }
                                           }

                                           @media (prefers-reduced-motion: reduce) {
                                               .login-page::before,
                                               .login-shell,
                                               .brand-orbit { animation: none; }
                                           }
                                           """;

    private static readonly string ApplicationVersion =
        HtmlEncoder.Default.Encode(FormatVersion(ThisAssembly.AssemblyInformationalVersion));

    private static readonly string LoginPage = BuildLoginPage();

    private static readonly string LoginPageWithError = BuildLoginPage(
        """<p class="error" role="alert">Authentication failed</p>""");

    private static readonly string LogoutPage = $"""
                                                 <!doctype html>
                                                 <html lang="en">
                                                 <head>
                                                     <meta charset="utf-8">
                                                     <meta name="viewport" content="width=device-width, initial-scale=1">
                                                     <title>RasHub API · Sign out</title>
                                                     <style>{PageStyles}</style>
                                                 </head>
                                                 <body>
                                                     <main class="portal portal-compact">
                                                         <section class="terminal">
                                                             <div class="eyebrow">
                                                                 <div class="identity">
                                                                     <span>RasHub.Web / {ApplicationVersion}</span>
                                                                     <span class="environment">Development environment</span>
                                                                 </div>
                                                                 <span class="status">Session active</span>
                                                             </div>
                                                             <header>
                                                                 <span class="prompt" aria-hidden="true">&gt;</span>
                                                                 <h1>RasHub</h1>
                                                                 <span class="cursor" aria-hidden="true"></span>
                                                             </header>
                                                             <p class="context">API documentation / Active session</p>
                                                             <p class="message">Terminate documentation session?</p>
                                                             <form method="post" action="/swagger/logout" class="actions">
                                                                 <button type="submit">[ Sign out ]</button>
                                                                 <a href="/swagger/">[ Cancel ]</a>
                                                             </form>
                                                         </section>
                                                     </main>
                                                 </body>
                                                 </html>
                                                 """;

    private static string FormatVersion(string version)
    {
        var parts = version.Split('+', 2);

        if (parts.Length == 2)
        {
            var commit = parts[1];

            if (commit.Length > 7)
                commit = commit[..7];

            return $"{parts[0]}+{commit}";
        }

        return version;
    }

    public static IEndpointRouteBuilder MapApiDocumentationAuthentication(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                ApiDocumentationAuthenticationDefaults.LoginPath,
                ShowLoginPage)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapPost(
                ApiDocumentationAuthenticationDefaults.LoginPath,
                SignInAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapGet(
                ApiDocumentationAuthenticationDefaults.LogoutPath,
                ShowLogoutPage)
            .RequireAuthorization(
                ApiDocumentationAuthenticationDefaults.Policy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                ApiDocumentationAuthenticationDefaults.LogoutPath,
                SignOutAsync)
            .RequireAuthorization(
                ApiDocumentationAuthenticationDefaults.Policy)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IResult ShowLoginPage(HttpContext context)
    {
        ConfigureLoginResponse(context.Response);
        return Results.Content(LoginPage, "text/html; charset=utf-8");
    }

    private static IResult ShowLogoutPage(HttpContext context)
    {
        ConfigureLoginResponse(context.Response);
        return Results.Content(LogoutPage, "text/html; charset=utf-8");
    }

    private static async Task<IResult> SignInAsync(
        HttpContext context,
        IOptions<ApiDocumentationOptions> options)
    {
        ConfigureLoginResponse(context.Response);

        if (!context.Request.HasFormContentType)
            return Results.Content(
                LoginPageWithError,
                "text/html; charset=utf-8",
                statusCode: StatusCodes.Status400BadRequest);

        var form = await context.Request.ReadFormAsync(
            context.RequestAborted);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var credentials = options.Value;

        if (!CredentialsEqual(credentials.Username, username) |
            !CredentialsEqual(credentials.Password, password))
            return Results.Content(
                LoginPageWithError,
                "text/html; charset=utf-8",
                statusCode: StatusCodes.Status401Unauthorized);

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, credentials.Username)],
            ApiDocumentationAuthenticationDefaults.Scheme);

        await context.SignInAsync(
            ApiDocumentationAuthenticationDefaults.Scheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false
            });

        return Results.Redirect("/swagger/");
    }

    private static async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(
            ApiDocumentationAuthenticationDefaults.Scheme);

        context.Response.Redirect(
            ApiDocumentationAuthenticationDefaults.LoginPath);
    }

    private static bool CredentialsEqual(
        string expected,
        string provided)
    {
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(provided));

        return CryptographicOperations.FixedTimeEquals(
            expectedHash,
            providedHash);
    }

    private static void ConfigureLoginResponse(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache";
        response.Headers.Pragma = "no-cache";
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; " +
            "form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static string BuildLoginPage(string error = "")
    {
        return $"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>RasHub API · Sign in</title>
                    <style>{LoginPageStyles}</style>
                </head>
                <body class="login-page">
                    <div class="cosmos" aria-hidden="true">
                        <span class="world-orbit world-orbit-one"></span>
                        <span class="world-orbit world-orbit-two"></span>
                        <span class="world-orbit world-orbit-three"></span>
                    </div>
                    <main class="login-shell">
                        <section class="login-content">
                            <div class="brand-mark" aria-hidden="true">
                                <span class="brand-orbit brand-orbit-one"><i></i></span>
                                <span class="brand-orbit brand-orbit-two"><i></i></span>
                                <span class="brand-orbit brand-orbit-three"><i></i></span>
                                <span class="brand-core"></span>
                            </div>
                            <h1>RasHub</h1>
                            <p class="subtitle">Development Environment</p>
                            <div class="motto" aria-hidden="true">
                                <span></span>
                                <p>v{ApplicationVersion}</p>
                                <span></span>
                            </div>
                            {error}
                            <form method="post" action="/swagger/login" class="login-card">
                                <div class="field">
                                    <svg viewBox="0 0 24 24" aria-hidden="true">
                                        <circle cx="12" cy="8" r="4"></circle>
                                        <path d="M4.5 21a7.5 7.5 0 0 1 15 0"></path>
                                    </svg>
                                    <input id="username" name="username" type="text" placeholder="Username" aria-label="Username" autocomplete="username" autofocus required>
                                </div>
                                <div class="field">
                                    <svg viewBox="0 0 24 24" aria-hidden="true">
                                        <rect x="5" y="10" width="14" height="11" rx="1"></rect>
                                        <path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3"></path>
                                    </svg>
                                    <input id="password" name="password" type="password" placeholder="Password" aria-label="Password" autocomplete="current-password" required>
                                </div>
                                <button type="submit">
                                    <span>Sign In</span>
                                </button>
                            </form>
                        </section>
                    </main>
                </body>
                </html>
                """;
    }
}