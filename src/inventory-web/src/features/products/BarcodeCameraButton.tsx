import { useEffect, useRef, useState } from "react";

type BarcodeDetection = { rawValue?: string | null };
type BarcodeDetectorInstance = {
  detect(source: ImageBitmapSource): Promise<BarcodeDetection[]>;
};
type BarcodeDetectorConstructor = new (options?: { formats?: string[] }) => BarcodeDetectorInstance;

declare global {
  interface Window {
    BarcodeDetector?: BarcodeDetectorConstructor;
    webkitAudioContext?: typeof AudioContext;
  }
}

function beep() {
  const AudioContextCtor = window.AudioContext ?? window.webkitAudioContext;
  if (!AudioContextCtor) {
    return;
  }

  try {
    const context = new AudioContextCtor();
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.connect(gain);
    gain.connect(context.destination);
    oscillator.type = "sine";
    oscillator.frequency.value = 880;
    gain.gain.value = 0.05;
    oscillator.start();

    window.setTimeout(() => {
      try {
        oscillator.stop();
      } catch (error) {
        void error;
      }
      void context.close().catch(() => undefined);
    }, 120);
  } catch (error) {
    void error;
  }
}

function haptic() {
  if (typeof navigator.vibrate === "function") {
    navigator.vibrate(50);
  }
}

export function BarcodeCameraButton(props: { onDetected?: (value: string) => void }) {
  const supported = Boolean(window.BarcodeDetector);
  const [active, setActive] = useState(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const detectorRef = useRef<BarcodeDetectorInstance | null>(null);

  function stop() {
    setActive(false);
    const video = videoRef.current;
    if (video) {
      try {
        void video.pause();
      } catch (error) {
        void error;
      }
      video.srcObject = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }
    detectorRef.current = null;
  }

  useEffect(() => {
    return () => {
      stop();
    };
  }, []);

  async function start() {
    if (!supported || !window.BarcodeDetector) return;
    try {
      detectorRef.current = new window.BarcodeDetector({ formats: ["ean_13", "ean_8", "code_128"] });
      streamRef.current = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
      const video = videoRef.current;
      if (video) {
        video.srcObject = streamRef.current;
        await video.play();
      }
      setActive(true);
      requestAnimationFrame(scan);
    } catch (error) {
      void error;
      stop();
    }
  }

  async function scan() {
    const video = videoRef.current;
    const detector = detectorRef.current;
    if (!active || !video || !detector) return;
    try {
      const bitmap = await createImageBitmap(video);
      const codes = await detector.detect(bitmap);
      bitmap.close?.();
      if (codes && codes.length > 0) {
        const raw = String(codes[0].rawValue ?? "").trim();
        if (raw) {
          beep();
          haptic();
          props.onDetected?.(raw);
          stop();
          return;
        }
      }
    } catch (error) {
      void error;
    }
    requestAnimationFrame(scan);
  }

  if (!supported) {
    return <button type="button" disabled title="BarcodeDetector non supporté">Caméra non supportée</button>;
  }

  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
      <button type="button" onClick={() => (active ? stop() : start())}>
        {active ? "Arrêter caméra" : "Activer caméra"}
      </button>
      {active && <video ref={videoRef} style={{ width: 160, height: 120, background: "#000" }} muted />}
    </span>
  );
}
