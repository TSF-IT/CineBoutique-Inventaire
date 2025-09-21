import { Outlet } from 'react-router-dom'
import { Button } from '../../components/Button'
import { Page } from '../../components/Page'
import { useAuth } from '../../contexts/AuthContext'

export const AdminLayout = () => {
  const { isAuthenticated, user, logout } = useAuth()

  return (
    <Page className="gap-6">
      <header className="flex items-center justify-between">
        <div>
          <p className="text-sm uppercase tracking-[0.3em] text-brand-200">CinéBoutique</p>
          <h1 className="text-3xl font-bold text-white">Administration</h1>
          <p className="text-sm text-slate-400">Gérez les zones d&apos;inventaire et les accès.</p>
        </div>
        {isAuthenticated && (
          <div className="flex flex-col items-end gap-2 text-right text-sm text-slate-300">
            <span>{user?.name}</span>
            <Button variant="ghost" onClick={logout}>
              Se déconnecter
            </Button>
          </div>
        )}
      </header>
      <Outlet />
    </Page>
  )
}
