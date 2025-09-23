import { Outlet } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Page } from '../../components/Page'
import { ThemeToggle } from '../../components/ThemeToggle'
import { useAuth } from '../../contexts/AuthContext'

export const AdminLayout = () => {
  const { isAuthenticated, user, logout } = useAuth()

  return (
    <Page className="gap-6">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-sm uppercase tracking-[0.3em] text-brand-500 dark:text-brand-200">CinéBoutique</p>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Administration</h1>
          <p className="text-sm text-slate-600 dark:text-slate-400">Gérez les zones d&apos;inventaire et les accès.</p>
        </div>
        <div className="flex flex-col items-end gap-3 sm:items-center">
          <ThemeToggle />
          {isAuthenticated && (
            <div className="flex flex-col items-end gap-2 text-right text-sm text-slate-600 dark:text-slate-300">
              <span>{user?.name}</span>
              <Button variant="ghost" onClick={logout}>
                Se déconnecter
              </Button>
            </div>
          )}
        </div>
      </header>
      <Outlet />
    </Page>
  )
}
