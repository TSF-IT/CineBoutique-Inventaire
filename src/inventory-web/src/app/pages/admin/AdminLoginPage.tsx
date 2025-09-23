import type { FormEvent } from 'react'
import { useState } from 'react'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { useAuth } from '../../contexts/AuthContext'

export const AdminLoginPage = () => {
  const { login, loading } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    try {
      await login(username, password)
    } catch {
      setError('Identifiants invalides ou droits insuffisants.')
    }
  }

  return (
    <Card className="max-w-md">
      <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Connexion</h2>
      <p className="text-sm text-slate-600 dark:text-slate-400">Accès réservé aux administrateurs de CinéBoutique.</p>
      <form className="mt-6 flex flex-col gap-4" onSubmit={handleSubmit}>
        <Input
          label="Identifiant"
          name="username"
          value={username}
          autoComplete="username"
          onChange={(event) => setUsername(event.target.value)}
        />
        <Input
          label="Mot de passe"
          name="password"
          type="password"
          value={password}
          autoComplete="current-password"
          onChange={(event) => setPassword(event.target.value)}
        />
        {error && <p className="text-sm text-red-600 dark:text-red-300">{error}</p>}
        <Button type="submit" disabled={loading} className="py-3">
          {loading ? 'Connexion…' : 'Se connecter'}
        </Button>
      </form>
    </Card>
  )
}
