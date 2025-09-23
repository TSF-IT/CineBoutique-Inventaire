import type { ErrorInfo, ReactNode } from 'react'
import { Component } from 'react'
import { Button } from './ui/Button'
import { Card } from './Card'

interface AppErrorBoundaryProps {
  children: ReactNode
}

interface AppErrorBoundaryState {
  hasError: boolean
  error?: Error
}

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  public state: AppErrorBoundaryState = {
    hasError: false,
    error: undefined,
  }

  static getDerivedStateFromError(error: Error): AppErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    if (import.meta.env.DEV) {
      console.error('Erreur non gérée dans l’interface', error, errorInfo)
    }
  }

  private handleReset = () => {
    this.setState({ hasError: false, error: undefined })
    window.location.assign('/')
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex min-h-screen items-center justify-center bg-slate-50 p-4 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
          <Card className="max-w-lg text-center">
            <h1 className="text-2xl font-semibold">Une erreur est survenue</h1>
            <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
              L&apos;application a rencontré une erreur inattendue. Vous pouvez revenir à l&apos;accueil et réessayer.
            </p>
            {import.meta.env.DEV && this.state.error && (
              <pre className="mt-4 overflow-x-auto rounded-xl bg-slate-900/80 p-4 text-left text-xs text-red-200 dark:bg-slate-900/60">
                {this.state.error.message}
              </pre>
            )}
            <div className="mt-6 flex justify-center">
              <Button onClick={this.handleReset}>Retour à l&apos;accueil</Button>
            </div>
          </Card>
        </div>
      )
    }

    return this.props.children
  }
}
