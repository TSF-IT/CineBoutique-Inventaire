import {
  ChangeEvent,
  DragEvent,
  forwardRef,
  KeyboardEvent,
  useId,
  useImperativeHandle,
  useRef,
  useState,
} from 'react'
import clsx from 'clsx'

type FileUploadFieldProps = {
  name: string
  label: string
  accept?: string
  description?: string
  helpText?: string
  disabled?: boolean
  file: File | null
  onFileSelected?: (file: File | null) => void
}

export const FileUploadField = forwardRef<HTMLInputElement, FileUploadFieldProps>(
  (
    { name, label, accept, description, helpText, disabled = false, file, onFileSelected },
    forwardedRef,
  ) => {
    const autoId = useId()
    const inputRef = useRef<HTMLInputElement | null>(null)
    const [isDragging, setIsDragging] = useState(false)

    useImperativeHandle(forwardedRef, () => inputRef.current as HTMLInputElement | null)

    const handleFiles = (files: FileList | null) => {
      if (!onFileSelected) {
        return
      }
      const nextFile = files?.[0] ?? null
      onFileSelected(nextFile)

      if (!nextFile) {
        if (inputRef.current) {
          inputRef.current.value = ''
        }
        return
      }

      if (inputRef.current && typeof DataTransfer !== 'undefined') {
        try {
          const dataTransfer = new DataTransfer()
          dataTransfer.items.add(nextFile)
          inputRef.current.files = dataTransfer.files
        } catch {
          inputRef.current.value = ''
        }
      }
    }

    const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
      handleFiles(event.target.files)
    }

    const openFileDialog = () => {
      if (disabled) {
        return
      }
      inputRef.current?.click()
    }

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
      if (event.key === ' ' || event.key === 'Enter') {
        event.preventDefault()
        openFileDialog()
      }
    }

    const handleDrop = (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault()
      event.stopPropagation()
      setIsDragging(false)
      if (disabled) {
        return
      }
      handleFiles(event.dataTransfer?.files ?? null)
    }

    const handleDragOver = (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault()
      if (disabled) {
        return
      }
      setIsDragging(true)
    }

    const handleDragLeave = (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault()
      const nextTarget = event.relatedTarget
      if (nextTarget instanceof Node && event.currentTarget.contains(nextTarget)) {
        return
      }
      setIsDragging(false)
    }

    const handleClear = () => {
      if (disabled) {
        return
      }
      if (inputRef.current) {
        inputRef.current.value = ''
      }
      onFileSelected?.(null)
    }

    const dropZoneId = `${name}-${autoId}`
    const labelId = `${dropZoneId}-label`
    const descriptionId = description ? `${dropZoneId}-description` : undefined

    return (
      <div className="flex flex-col gap-2">
        <label
          id={labelId}
          htmlFor={`${dropZoneId}-input`}
          className="text-sm font-medium text-slate-700 dark:text-slate-200"
        >
          {label}
        </label>
        <input
          ref={inputRef}
          id={`${dropZoneId}-input`}
          name={name}
          type="file"
          accept={accept}
          className="sr-only"
          onChange={handleChange}
          disabled={disabled}
        />
        <div
          id={dropZoneId}
          role="button"
          tabIndex={disabled ? -1 : 0}
          onClick={openFileDialog}
          onKeyDown={handleKeyDown}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
          onDragEnter={handleDragOver}
          onDragLeave={handleDragLeave}
          aria-disabled={disabled}
          aria-labelledby={labelId}
          aria-describedby={descriptionId}
          className={clsx(
            'relative flex flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed px-6 py-10 text-center transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400',
            disabled
              ? 'cursor-not-allowed border-slate-200 bg-slate-50 text-slate-400'
              : 'cursor-pointer border-slate-300 bg-slate-50/80 text-slate-600 hover:border-brand-300 hover:bg-brand-50/40 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-300',
            isDragging && !disabled && 'border-brand-400 bg-brand-50/70 text-brand-600',
          )}
        >
          <div className="flex flex-col gap-1">
            <span className="text-base font-semibold text-slate-800 dark:text-white">
              {file ? 'Fichier prêt à importer' : 'Déposez votre CSV ici'}
            </span>
            <span className="text-sm text-slate-600 dark:text-slate-300">
              {file ? file.name : '…ou cliquez pour parcourir vos dossiers'}
            </span>
            {description && (
              <span id={descriptionId} className="text-xs text-slate-500 dark:text-slate-400">
                {description}
              </span>
            )}
          </div>
          {file && !disabled && (
            <button
              type="button"
              onClick={handleClear}
              className="absolute right-4 top-4 rounded-full bg-slate-900/80 px-3 py-1 text-xs font-semibold text-white shadow-md transition hover:bg-slate-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/70 dark:bg-slate-700 dark:hover:bg-slate-600"
            >
              Retirer
            </button>
          )}
        </div>
        {helpText && <p className="text-xs text-slate-500 dark:text-slate-400">{helpText}</p>}
      </div>
    )
  },
)

FileUploadField.displayName = 'FileUploadField'
