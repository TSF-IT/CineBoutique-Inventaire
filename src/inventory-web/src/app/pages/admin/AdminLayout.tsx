import { Link, Outlet } from 'react-router-dom'
import { Page } from '../../components/Page'
import { useShop } from '@/state/ShopContext'

export const AdminLayout = () => {
  const { shop } = useShop()
  const shopDisplayName = shop?.name?.trim()

  return (
    <Page className="gap-6">
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
        <Link
          to="/"
          className="inline-flex items-center justify-center gap-2 self-start rounded-2xl border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 transition-colors duration-150 hover:bg-slate-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 dark:border-slate-700 dark:text-white dark:hover:bg-slate-800"
        >
          Retour à l&apos;accueil
        </Link>
      </header>
      <Outlet />
    </Page>
  )
}
