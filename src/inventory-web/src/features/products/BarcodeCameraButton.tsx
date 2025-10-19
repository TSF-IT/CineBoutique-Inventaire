import { useEffect, useRef, useState } from "react";

declare global { interface Window { BarcodeDetector?: any; } }

export function BarcodeCameraButton(props: { onDetected?: (value: string) => void; }) {
  const [supported, setSupported] = useState<boolean>(false);
  const [active, setActive] = useState<boolean>(false);
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const detectorRef = useRef<any>(null);

  useEffect(() => { setSupported(!!window.BarcodeDetector); return () => stop(); }, []);

  async function start() {
    if (!window.BarcodeDetector) return;
    try {
      detectorRef.current = new window.BarcodeDetector({ formats: ["ean_13","ean_8","code_128"] });
      streamRef.current = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
      if (videoRef.current) { videoRef.current.srcObject = streamRef.current; await videoRef.current.play(); }
      setActive(true);
      requestAnimationFrame(scan);
    } catch { stop(); }
  }

  async function scan() {
    if (!active || !videoRef.current || !detectorRef.current) return;
    try {
      const bitmap = await createImageBitmap(videoRef.current as any);
      const codes = await detectorRef.current.detect(bitmap);
      if (codes && codes.length > 0) {
        const raw = String(codes[0].rawValue || "").trim();
        if (raw) { props.onDetected?.(raw); stop(); return; }
      }
    } catch { /* ignore */ }
    requestAnimationFrame(scan);
  }

  function stop() {
    setActive(false);
    if (videoRef.current) { try { videoRef.current.pause(); } catch {} (videoRef.current as any).srcObject = null; }
    if (streamRef.current) { streamRef.current.getTracks().forEach(t => t.stop()); streamRef.current=null; }
  }

  if (!supported) return <button type="button" disabled title="BarcodeDetector non supporté">Caméra non supportée</button>;

  return (
    <span style={{ display:"inline-flex", alignItems:"center", gap:8 }}>
      <button type="button" onClick={() => active ? stop() : start()}>
        {active ? "Arrêter caméra" : "Activer caméra"}
      </button>
      {active && <video ref={videoRef} style={{ width:160, height:120, background:"#000" }} muted />}
    </span>
  );
}
