export function DeploymentFooter({ label, sha }: { label: string; sha: string }) {
  return (
    <footer className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2 border-t border-line px-5 py-4 text-[11px] text-faint sm:px-8">
      <span>TaskFlow <span className="mx-1.5 opacity-50">/</span> demo fixture · no real data</span>
      <span>{label} · deployed with Dokploy · <span className="rounded-md border border-line bg-surface px-1.5 py-0.5 font-mono text-muted">{sha}</span></span>
    </footer>
  )
}
