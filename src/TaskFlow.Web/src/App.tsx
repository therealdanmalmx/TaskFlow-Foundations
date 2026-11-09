import { TaskFlowApp } from './app/TaskFlowApp'

// These two values intentionally stay in this root component. The deployment
// lesson changes the visible version marker while CI injects the commit SHA.
const deploymentLabel = 'JWT auth · v1'
const sha = (import.meta.env.VITE_GIT_SHA ?? 'dev').slice(0, 7)

export default function App() {
  return <TaskFlowApp deploymentLabel={deploymentLabel} sha={sha} />
}
