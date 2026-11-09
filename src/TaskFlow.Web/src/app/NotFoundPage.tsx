import { FileQuestion } from 'lucide-react'
import { Link } from 'react-router-dom'
import { EmptyState } from '../shared/components/EmptyState'

export function NotFoundPage() {
  return (
    <div className="mx-auto max-w-3xl py-12">
      <EmptyState
        icon={FileQuestion}
        title="Page not found"
        description="The page may have moved, or you may not have access to it."
        action={<Link to="/" className="font-semibold text-brand hover:underline">Return to your boards</Link>}
      />
    </div>
  )
}
