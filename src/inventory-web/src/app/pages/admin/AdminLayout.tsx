import { Outlet } from 'react-router-dom'
import { Page } from '../../components/Page'
import { useShop } from '@/state/ShopContext'

export const AdminLayout = () => {
  const { shop } = useShop()
  const shopDisplayName = shop?.name?.trim()

  return (
    <Page className="gap-6" showHomeLink>
      <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-sm uppercase tracking-[0.3em] text-brand-500 dark:text-brand-200">
            {shopDisplayName ?? 'CinéBoutique'}
          </p>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Administration</h1>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            Paramétrez les zones d&apos;inventaire et les comptes utilisateurs depuis un même espace.
          </p>
        </div>
      </header>
      <Outlet />
    </Page>
  )
}
