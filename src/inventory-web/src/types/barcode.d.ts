export {}

declare global {
  interface BarcodeDetectorResult {
    rawValue: string
    format?: string
  }

  interface BarcodeDetectorOptions {
    formats?: string[]
  }

  interface BarcodeDetector {
    detect: (source: ImageBitmapSource) => Promise<BarcodeDetectorResult[]>
  }

  interface BarcodeDetectorConstructor {
    new (options?: BarcodeDetectorOptions): BarcodeDetector
    getSupportedFormats?: () => Promise<string[]>
  }

  interface Window {
    BarcodeDetector?: BarcodeDetectorConstructor
  }
}
