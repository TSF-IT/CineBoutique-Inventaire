import { Outlet } from 'react-router-dom'

import { Page } from '../../components/Page'

import { useShop } from '@/state/ShopContext'

export const AdminLayout = () => {
  const { shop } = useShop()
  const shopDisplayName = shop?.name?.trim()

  return (
    <Page className="gap-10" showHomeLink>
      <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.4em] text-brand-500/90 dark:text-brand-200/90">
            {shopDisplayName ?? 'CinéBoutique'}
          </p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight text-(--cb-text)">Administration</h1>
          <p className="mt-2 max-w-xl text-sm leading-relaxed text-(--cb-muted)">
            Paramétrez les zones d&apos;inventaire et les comptes utilisateurs depuis un même espace.
          </p>
        </div>
      </header>
      <Outlet />
    </Page>
  )
}
