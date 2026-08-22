import { useEffect, useRef } from "react";
import "./CursorWisp.css";

// Un feu follet qui orbite paresseusement autour du curseur, avec un léger retard
// et un mouvement organique — inspiré des wisps qu'on croise dans Valheim.
export function CursorWisp() {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      return;
    }

    let mouseX = window.innerWidth / 2;
    let mouseY = window.innerHeight / 2;
    let posX = mouseX;
    let posY = mouseY;
    let angle = Math.random() * Math.PI * 2;
    let visible = false;

    function onMove(e: MouseEvent) {
      mouseX = e.clientX;
      mouseY = e.clientY;
      visible = true;
    }
    function onLeave() {
      visible = false;
    }

    window.addEventListener("mousemove", onMove);
    document.addEventListener("mouseleave", onLeave);

    let raf: number;
    function tick() {
      angle += 0.025;
      const radius = 24 + Math.sin(angle * 0.6) * 8;
      const targetX = mouseX + Math.cos(angle) * radius;
      const targetY = mouseY + Math.sin(angle) * radius;

      posX += (targetX - posX) * 0.08;
      posY += (targetY - posY) * 0.08;

      const el = ref.current;
      if (el) {
        el.style.transform = `translate3d(${posX}px, ${posY}px, 0) translate(-50%, -50%)`;
        el.style.opacity = visible ? "1" : "0";
      }
      raf = requestAnimationFrame(tick);
    }
    raf = requestAnimationFrame(tick);

    return () => {
      window.removeEventListener("mousemove", onMove);
      document.removeEventListener("mouseleave", onLeave);
      cancelAnimationFrame(raf);
    };
  }, []);

  return (
    <div className="cursor-wisp" ref={ref} aria-hidden="true">
      <div className="cursor-wisp__core" />
    </div>
  );
}
