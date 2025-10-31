import {
  BrowserMultiFormatReader,
  type IScannerControls,
} from "@zxing/browser";
import {
  BarcodeFormat,
  DecodeHintType,
  NotFoundException,
} from "@zxing/library";
import { clsx } from 'clsx'
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type MutableRefObject,
} from "react";

type SupportedFormat =
  | "EAN_13"
  | "EAN_8"
  | "CODE_128"
  | "CODE_39"
  | "ITF"
  | "QR_CODE";
type DetectorFormat =
  | "ean_13"
  | "ean_8"
  | "code_128"
  | "code_39"
  | "itf"
  | "qr_code";
type TorchCapabilities = MediaTrackCapabilities & { torch?: boolean };
type TorchConstraintSet = MediaTrackConstraintSet & { torch?: boolean };
type FocusConstraintSet = MediaTrackConstraintSet & { focusMode?: string };

type ExternalCameraBinding = {
  videoRef: MutableRefObject<HTMLVideoElement | null>;
  active: boolean;
  error: unknown;
};

interface BarcodeScannerProps {
  active: boolean;
  onDetected: (value: string) => void | Promise<void>;
  onError?: (reason: string) => void;
  onPickImage?: (file: File) => void;
  enableTorchToggle?: boolean;
  preferredFormats?: SupportedFormat[];
  presentation?: "embedded" | "immersive";
  camera?: ExternalCameraBinding;
}

const DEFAULT_FORMATS: SupportedFormat[] = [
  "EAN_13",
  "EAN_8",
  "CODE_128",
  "CODE_39",
  "ITF",
  "QR_CODE",
];

const ZXING_FORMATS: Record<SupportedFormat, BarcodeFormat> = {
  EAN_13: BarcodeFormat.EAN_13,
  EAN_8: BarcodeFormat.EAN_8,
  CODE_128: BarcodeFormat.CODE_128,
  CODE_39: BarcodeFormat.CODE_39,
  ITF: BarcodeFormat.ITF,
  QR_CODE: BarcodeFormat.QR_CODE,
};

const DETECTOR_FORMATS: Record<SupportedFormat, DetectorFormat> = {
  EAN_13: "ean_13",
  EAN_8: "ean_8",
  CODE_128: "code_128",
  CODE_39: "code_39",
  ITF: "itf",
  QR_CODE: "qr_code",
};

const CAMERA_CONSTRAINTS: MediaStreamConstraints = {
  video: {
    facingMode: { ideal: "environment" },
    width: { ideal: 1280, min: 640 },
    height: { ideal: 720, min: 480 },
    advanced: [{ focusMode: "continuous" }] as FocusConstraintSet[],
  } satisfies MediaTrackConstraints,
};

const HELP_TIMEOUT_MS = 9000;

type StreamEnabledVideo = HTMLVideoElement & { srcObject: MediaStream | null };

const setVideoStream = (
  video: HTMLVideoElement,
  stream: MediaStream | null
) => {
  (video as StreamEnabledVideo).srcObject = stream;
};

const getVideoStream = (video: HTMLVideoElement) => {
  const source = (video as StreamEnabledVideo).srcObject;
  if (typeof MediaStream !== "undefined" && source instanceof MediaStream) {
    return source;
  }
  return null;
};

const isCameraAvailable = () => {
  if (typeof window === "undefined" || typeof navigator === "undefined") {
    return false;
  }
  if (!window.isSecureContext) {
    return false;
  }
  if (!("mediaDevices" in navigator)) {
    return false;
  }
  return typeof navigator.mediaDevices?.getUserMedia === "function";
};

const createHints = (formats: SupportedFormat[]) => {
  const hints = new Map();
  hints.set(
    DecodeHintType.POSSIBLE_FORMATS,
    formats.map((format) => ZXING_FORMATS[format])
  );
  hints.set(DecodeHintType.TRY_HARDER, true);
  return hints;
};

export const BarcodeScanner = ({
  active,
  onDetected,
  onError,
  onPickImage,
  enableTorchToggle = true,
  preferredFormats,
  presentation = "embedded",
  camera,
}: BarcodeScannerProps) => {
  const [status, setStatus] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [torchSupported, setTorchSupported] = useState(false);
  const [torchEnabled, setTorchEnabled] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const internalVideoRef = useRef<HTMLVideoElement | null>(null);
  const usingExternalCamera = Boolean(camera);
  const videoRef = camera?.videoRef ?? internalVideoRef;
  const externalCameraActive = camera?.active ?? false;
  const externalCameraError = camera?.error;
  const controlsRef = useRef<IScannerControls | null>(null);
  const readerRef = useRef<BrowserMultiFormatReader | null>(null);
  const animationFrameRef = useRef<number | null>(null);
  const helpTimeoutRef = useRef<number | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const activeRef = useRef(false);
  const lastZxingErrorLogRef = useRef(0);
  const processingRef = useRef(false);
  const restartDetectionRef = useRef<(() => Promise<void>) | null>(null);

  const getCurrentStream = useCallback(() => {
    const video = videoRef.current;
    if (!video) {
      return null;
    }
    return getVideoStream(video);
  }, [videoRef]);

  const stopCurrentStream = useCallback(() => {
    const stream = getCurrentStream();
    if (!stream) {
      return;
    }
    for (const track of stream.getTracks()) {
      try {
        track.stop();
      } catch (error) {
        if (import.meta.env.DEV) {
          console.debug("[scanner] Impossible d’arrêter une piste caméra", error);
        }
      }
    }
    const video = videoRef.current;
    if (video) {
      try {
        video.pause();
      } catch (error) {
        if (import.meta.env.DEV) {
          console.debug("[scanner] Pause vidéo impossible", error);
        }
      }
      // Libère la référence (notamment sur iOS)
      setVideoStream(video, null);
      video.removeAttribute("src");
    }
  }, [getCurrentStream, videoRef]);

  const scheduleHelp = useCallback(() => {
    if (helpTimeoutRef.current) {
      window.clearTimeout(helpTimeoutRef.current);
    }
    helpTimeoutRef.current = window.setTimeout(() => {
      setShowHelp(true);
      setStatus("Aucune détection, rapprochez-vous ou améliorez l’éclairage.");
    }, HELP_TIMEOUT_MS);
  }, []);

  const formats = useMemo(() => {
    const preferred =
      preferredFormats && preferredFormats.length > 0
        ? preferredFormats
        : DEFAULT_FORMATS;
    const unique = new Set(preferred ?? DEFAULT_FORMATS);
    return Array.from(unique);
  }, [preferredFormats]);

  const hints = useMemo(() => createHints(formats), [formats]);

  const cleanupAnimation = useCallback(() => {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current);
      animationFrameRef.current = null;
    }
  }, []);

  const cleanupControls = useCallback(() => {
    controlsRef.current?.stop();
    controlsRef.current = null;
    readerRef.current = null;
  }, []);

  const cleanup = useCallback(() => {
    cleanupAnimation();
    cleanupControls();
    if (!usingExternalCamera) {
      stopCurrentStream();
    }
    if (helpTimeoutRef.current) {
      window.clearTimeout(helpTimeoutRef.current);
      helpTimeoutRef.current = null;
    }
    restartDetectionRef.current = null;
    processingRef.current = false;
  }, [cleanupAnimation, cleanupControls, stopCurrentStream, usingExternalCamera]);

  const dispatchError = useCallback(
    (message: string) => {
      setError(message);
      onError?.(message);
    },
    [onError]
  );

  const handleDetectedValue = useCallback(
    async (value: string, source: "detector" | "zxing") => {
      if (!value || processingRef.current) {
        return;
      }
      processingRef.current = true;
      cleanupAnimation();
      cleanupControls();
      setShowHelp(false);
      setStatus("Code détecté, traitement en cours…");
      try {
        await Promise.resolve(onDetected(value));
        setStatus("Visez le code-barres");
      } catch (error) {
        if (import.meta.env.DEV) {
          console.error("[scanner] onDetected a échoué", error);
        }
        dispatchError("Traitement du code détecté impossible. Réessayez.");
      } finally {
        processingRef.current = false;
        if (activeRef.current && restartDetectionRef.current) {
          try {
            await restartDetectionRef.current();
            if (activeRef.current) {
              scheduleHelp();
            }
          } catch (error) {
            if (import.meta.env.DEV) {
              console.error("[scanner] Redémarrage du scan impossible", error);
            }
            if (source === "zxing") {
              dispatchError(
                "Le lecteur s’est arrêté de manière inattendue. Réactivez la caméra."
              );
            }
          }
        }
      }
    },
    [cleanupAnimation, cleanupControls, dispatchError, onDetected, scheduleHelp]
  );

  const startBarcodeDetector = useCallback(async () => {
    const video = videoRef.current;
    if (
      !video ||
      !("BarcodeDetector" in window) ||
      typeof window.BarcodeDetector !== "function"
    ) {
      return false;
    }

    let detector: BarcodeDetector;
    try {
      detector = new window.BarcodeDetector({
        formats: formats.map((format) => DETECTOR_FORMATS[format]),
      });
    } catch (error) {
      if (import.meta.env.DEV) {
        console.warn(
          "[scanner] Instanciation BarcodeDetector impossible, fallback ZXing",
          error
        );
      }
      return false;
    }

    const ensureCanvas = () => {
      if (!canvasRef.current) {
        canvasRef.current = document.createElement("canvas");
      }
      const canvas = canvasRef.current;
      const context = canvas.getContext("2d", { willReadFrequently: true });
      if (!context) {
        throw new Error("Impossible de créer le contexte de lecture vidéo.");
      }
      return { canvas, context };
    };

    const loop = async () => {
      if (!activeRef.current) {
        return;
      }
      if (processingRef.current) {
        animationFrameRef.current = requestAnimationFrame(loop);
        return;
      }
      const ready = video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA;
      if (!ready) {
        animationFrameRef.current = requestAnimationFrame(loop);
        return;
      }

      try {
        const { videoWidth, videoHeight } = video;
        if (videoWidth === 0 || videoHeight === 0) {
          animationFrameRef.current = requestAnimationFrame(loop);
          return;
        }

        const cropWidth = Math.floor(videoWidth * 0.8);
        const cropHeight = Math.floor(videoHeight * 0.6);
        const cropX = Math.floor((videoWidth - cropWidth) / 2);
        const cropY = Math.floor((videoHeight - cropHeight) / 2);

        const { canvas, context } = ensureCanvas();
        if (canvas.width !== cropWidth || canvas.height !== cropHeight) {
          canvas.width = cropWidth;
          canvas.height = cropHeight;
        }
        context.drawImage(
          video,
          cropX,
          cropY,
          cropWidth,
          cropHeight,
          0,
          0,
          canvas.width,
          canvas.height
        );

        const results = await detector.detect(canvas);
        const match = results.find((entry) => entry.rawValue);
        if (match?.rawValue) {
          await handleDetectedValue(match.rawValue, "detector");
          if (!activeRef.current) {
            return;
          }
        }
      } catch (error) {
        if (import.meta.env.DEV) {
          console.debug("[scanner] Échec détection BarcodeDetector", error);
        }
      }

      animationFrameRef.current = requestAnimationFrame(loop);
    };

    restartDetectionRef.current = async () => {
      if (!activeRef.current) {
        return;
      }
      await startBarcodeDetector();
    };
    animationFrameRef.current = requestAnimationFrame(loop);
    return true;
  }, [formats, handleDetectedValue, videoRef]);

  const startZxing = useCallback(
    async (deviceId?: string, useExistingStream = false) => {
      const video = videoRef.current;
      if (!video) {
        return;
      }
      const reader = new BrowserMultiFormatReader(hints);
      readerRef.current = reader;
      restartDetectionRef.current = async () => {
        if (!activeRef.current) {
          return;
        }
        await startZxing(deviceId, useExistingStream);
      };
      try {
        const callback = async (result, error) => {
          if (processingRef.current) {
            return;
          }
          if (result) {
            await handleDetectedValue(result.getText(), "zxing");
            return;
          }
          if (!error) {
            return;
          }
          if (error instanceof NotFoundException) {
            return;
          }
          const now = Date.now();
          if (now - lastZxingErrorLogRef.current > 2000) {
            lastZxingErrorLogRef.current = now;
            if (import.meta.env.DEV) {
              console.warn("[scanner] ZXing erreur", error);
            }
          }
        };
        const controls = useExistingStream
          ? await reader.decodeFromVideoElement(video, callback)
          : await reader.decodeFromVideoDevice(deviceId, video, callback);
        controlsRef.current = controls;
      } catch (error) {
        dispatchError(
          "Lecture du flux caméra impossible. Vérifiez les autorisations."
        );
        if (import.meta.env.DEV) {
          console.error("[scanner] Initialisation ZXing impossible", error);
        }
      }
    },
    [dispatchError, handleDetectedValue, hints, videoRef]
  );

  useEffect(() => {
    activeRef.current = active;

    if (!active) {
      cleanup();
      setStatus("");
      setError(null);
      setShowHelp(false);
      setTorchSupported(false);
      setTorchEnabled(false);
      if (!usingExternalCamera) {
        stopCurrentStream();
      }
      return;
    }

    if (!usingExternalCamera && !isCameraAvailable()) {
      cleanup();
      dispatchError(
        "Caméra indisponible (HTTPS requis ou navigateur incompatible)."
      );
      setShowHelp(true);
      return;
    }

    const mapCameraError = (err: unknown) => {
      if (err instanceof DOMException) {
        switch (err.name) {
          case "NotAllowedError":
            return "Accès caméra refusé. Autorisez la caméra dans votre navigateur.";
          case "NotFoundError":
          case "OverconstrainedError":
            return "Aucune caméra compatible détectée. Branchez ou sélectionnez un autre appareil.";
          default:
            return "Impossible d’ouvrir la caméra. Réessayez ou utilisez l’import d’image.";
        }
      }
      return "Impossible d’ouvrir la caméra. Réessayez ou utilisez l’import d’image.";
    };

    if (usingExternalCamera) {
      if (externalCameraError) {
        cleanup();
        dispatchError(mapCameraError(externalCameraError));
        setShowHelp(true);
        return;
      }

      if (!externalCameraActive) {
        setStatus("Initialisation caméra…");
        setShowHelp(false);
        return;
      }

      const stream = getCurrentStream();
      if (!stream) {
        setStatus("Initialisation caméra…");
        return;
      }

      const [track] = stream.getVideoTracks();
      const capabilities = track?.getCapabilities?.();
      const supportsTorch = Boolean(
        (capabilities as TorchCapabilities | undefined)?.torch
      );
      setTorchSupported(supportsTorch);
      setTorchEnabled(false);
      setStatus("Visez le code-barres");
      setError(null);
      setShowHelp(false);

      let cancelled = false;

      const runDetection = async () => {
        try {
          const started = await startBarcodeDetector();
          if (!started) {
            const deviceId = track?.getSettings?.().deviceId;
            await startZxing(deviceId, true);
          }
          if (!cancelled && activeRef.current) {
            scheduleHelp();
          }
        } catch (error) {
          dispatchError(
            "Lecture du flux caméra impossible. Vérifiez les autorisations."
          );
          if (import.meta.env.DEV) {
            console.error("[scanner] Initialisation ZXing impossible", error);
          }
        }
      };

      void runDetection();

      return () => {
        cancelled = true;
        cleanup();
      };
    }

    const run = async () => {
      setStatus("Initialisation caméra…");
      setError(null);
      setShowHelp(false);

      try {
        const stream = await navigator.mediaDevices.getUserMedia(
          CAMERA_CONSTRAINTS
        );
        if (!activeRef.current) {
          stream.getTracks().forEach((track) => {
            try {
              track.stop();
            } catch (error) {
              if (import.meta.env.DEV) {
                console.debug("[scanner] Arrêt piste caméra impossible", error);
              }
            }
          });
          return;
        }
        const video = videoRef.current;
        if (video) {
          // iOS/WebView : playsinline + mute pour lecture auto
          video.setAttribute("playsinline", "true");
          video.muted = true;
          setVideoStream(video, stream);
          try {
            await video.play();
          } catch (error) {
            if (import.meta.env.DEV) {
              console.debug(
                "[scanner] Lecture vidéo impossible (probable autoplay)",
                error
              );
            }
          }
        }

        const currentStream = getCurrentStream();
        const [track] = currentStream?.getVideoTracks() ?? [];
        const capabilities = track?.getCapabilities?.();
        const supportsTorch = Boolean(
          (capabilities as TorchCapabilities | undefined)?.torch
        );
        setTorchSupported(supportsTorch);
        setTorchEnabled(false);
        setStatus("Visez le code-barres");

        const started = await startBarcodeDetector();
        if (!started) {
          const deviceId = track?.getSettings?.().deviceId;
          await startZxing(deviceId, false);
        }
        scheduleHelp();
      } catch (error) {
        dispatchError(mapCameraError(error));
        if (import.meta.env.DEV) {
          console.error(
            "[scanner] Impossible d’ouvrir la caméra (mode interne)",
            error
          );
        }
      }
    };

    void run();

    return () => {
      cleanup();
    };
  }, [
    active,
    cleanup,
    dispatchError,
    externalCameraActive,
    externalCameraError,
    getCurrentStream,
    scheduleHelp,
    startBarcodeDetector,
    startZxing,
    stopCurrentStream,
    usingExternalCamera,
    videoRef,
  ]);

  const handleTorchToggle = useCallback(async () => {
    const stream = getCurrentStream();
    if (!stream) {
      return;
    }
    const [track] = stream.getVideoTracks();
    if (!track?.applyConstraints) {
      return;
    }
    const next = !torchEnabled;
    try {
      const constraints: MediaTrackConstraints = {
        advanced: [{ torch: next } as TorchConstraintSet],
      };
      await track.applyConstraints(constraints);
      setTorchEnabled(next);
    } catch (error) {
      if (import.meta.env.DEV) {
        console.warn(
          "[scanner] Impossible de changer l’état de la torche",
          error
        );
      }
      dispatchError("Activation de la lampe impossible sur cet appareil.");
    }
  }, [dispatchError, getCurrentStream, torchEnabled]);

  const handleImageChange = (event: ChangeEvent<HTMLInputElement>) => {
    if (!onPickImage) {
      return;
    }
    const [file] = Array.from(event.target.files ?? []);
    if (file) {
      onPickImage(file);
    }
    event.target.value = "";
  };

  const cameraAvailable = usingExternalCamera || isCameraAvailable();

  const containerClass =
    presentation === "immersive"
      ? "relative h-full w-full bg-black text-slate-100"
      : "relative overflow-hidden rounded-3xl border border-slate-700 bg-black/80 text-slate-100";

  const videoClass =
    presentation === "immersive"
      ? "h-full w-full object-cover"
      : "h-64 w-full object-contain bg-black";

  return (
    <div className={containerClass}>
      {cameraAvailable ? (
        <div
          className={
            presentation === "immersive"
              ? "relative h-full w-full"
              : "flex flex-col gap-3"
          }
        >
          <div className="relative h-full w-full">
            <video
              ref={videoRef}
              className={videoClass}
              muted
              playsInline
              autoPlay
            />
            <div
              className={clsx(
                "pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-2xl border-4 border-brand-400/70",
                presentation === "immersive"
                  ? "h-1/2 w-3/4 max-w-md"
                  : "h-32 w-64"
              )}
              aria-hidden
            />
            <div
              className="pointer-events-none absolute inset-0 bg-linear-to-t from-black/50 via-transparent to-black/40"
              aria-hidden
            />
          </div>
          {presentation !== "immersive" && (
            <div className="space-y-2 px-4 pb-4">
              {status && <p className="text-sm text-brand-200">{status}</p>}
              {error && <p className="text-sm text-red-300">{error}</p>}
              {!error && !status && active && (
                <p className="text-sm text-slate-200">Visez le code-barres.</p>
              )}
              {showHelp && (
                <p className="text-xs text-amber-200">
                  Aucune détection pour l’instant. Approchez le code, ajustez
                  l’éclairage ou importez une photo.
                </p>
              )}
              {enableTorchToggle && (
                <button
                  type="button"
                  className="rounded-xl border border-slate-500 px-3 py-2 text-sm font-semibold text-slate-100 transition hover:border-brand-400 disabled:cursor-not-allowed disabled:border-slate-700 disabled:text-slate-500"
                  onClick={() => void handleTorchToggle()}
                  disabled={!torchSupported}
                >
                  {torchSupported
                    ? torchEnabled
                      ? "Éteindre la lampe"
                      : "Allumer la lampe"
                    : "Lampe non prise en charge"}
                </button>
              )}
              {showHelp && onPickImage && (
                <label className="flex cursor-pointer flex-col gap-2 rounded-2xl border border-slate-500 bg-slate-800/40 p-3 text-sm text-slate-100 hover:border-brand-400">
                  <span className="font-medium">
                    Importer une photo du code-barres
                  </span>
                  <input
                    type="file"
                    accept="image/*"
                    capture="environment"
                    className="text-xs text-slate-300"
                    onChange={handleImageChange}
                  />
                </label>
              )}
            </div>
          )}
          {presentation === "immersive" && enableTorchToggle && (
            <button
              type="button"
              className="absolute right-4 top-4 rounded-full bg-black/60 px-3 py-2 text-sm font-semibold text-white backdrop-blur focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 disabled:cursor-not-allowed disabled:opacity-60"
              onClick={() => void handleTorchToggle()}
              disabled={!torchSupported}
            >
              {torchSupported
                ? torchEnabled
                  ? "Lampe off"
                  : "Lampe on"
                : "Lampe indisponible"}
            </button>
          )}
          {presentation === "immersive" && status && (
            <div className="pointer-events-none absolute inset-x-0 bottom-6 flex justify-center">
              <span className="rounded-full bg-black/60 px-3 py-1 text-xs font-semibold text-emerald-200 backdrop-blur">
                {status}
              </span>
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-3 p-4">
          <p className="text-sm font-semibold text-amber-200">
            Caméra indisponible (connexion non sécurisée ou navigateur
            incompatible).
          </p>
          <ul className="list-disc space-y-1 pl-5 text-sm text-slate-200">
            <li>Utilisez une URL en HTTPS</li>
            <li>Autorisez l’accès caméra dans le navigateur</li>
            <li>Sinon, importez une photo du code-barres ci-dessous</li>
          </ul>
          {onPickImage && (
            <label className="flex cursor-pointer flex-col gap-2 rounded-2xl border border-slate-500 bg-slate-800/40 p-3 text-sm text-slate-100 hover:border-brand-400">
              <span className="font-medium">
                Importer une photo du code-barres
              </span>
              <input
                type="file"
                accept="image/*"
                capture="environment"
                className="text-xs text-slate-300"
                onChange={handleImageChange}
              />
            </label>
          )}
        </div>
      )}
    </div>
  );
};
