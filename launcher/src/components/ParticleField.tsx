import { useMemo } from "react";
import "./ParticleField.css";

interface Particle {
  id: number;
  size: number;
  left: number;
  drift: number;
  duration: number;
  delay: number;
  pulseDuration: number;
  pulseDelay: number;
}

const PARTICLE_COUNT = 34;

function makeParticles(): Particle[] {
  return Array.from({ length: PARTICLE_COUNT }, (_, id) => ({
    id,
    size: 3 + Math.random() * 6,
    left: Math.random() * 100,
    drift: (Math.random() - 0.5) * 16,
    duration: 11 + Math.random() * 10,
    delay: -Math.random() * 20,
    pulseDuration: 1.6 + Math.random() * 1.6,
    pulseDelay: -Math.random() * 2,
  }));
}

// Feux follents ambiants qui montent en fond d'écran — écho des wisps qu'on croise
// dans les marais de Valheim la nuit. Purement décoratif, ignore les clics.
export function ParticleField() {
  const particles = useMemo(makeParticles, []);

  return (
    <div className="particle-field" aria-hidden="true">
      {particles.map((p) => (
        <div
          key={p.id}
          className="particle"
          style={
            {
              "--size": `${p.size}px`,
              "--left": `${p.left}vw`,
              "--drift": `${p.drift}vw`,
              "--duration": `${p.duration}s`,
              "--delay": `${p.delay}s`,
              "--pulse-duration": `${p.pulseDuration}s`,
              "--pulse-delay": `${p.pulseDelay}s`,
            } as React.CSSProperties
          }
        >
          <div className="particle__glow" />
        </div>
      ))}
    </div>
  );
}
