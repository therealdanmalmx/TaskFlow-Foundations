import { apiRequest, apiRequestWithResponse } from '../../../shared/api/HttpClient'

export interface BoardStats {
  todo: number
  doing: number
  done: number
  total: number
  doneToday: number
  oldestActiveAgeDays: number | null
  overdue: number
}

export type CacheStatus = 'HIT' | 'MISS' | 'OFF' | 'DOWN'

export interface StatsResult {
  stats: BoardStats
  cache: CacheStatus
}

export interface ActivityEntry {
  message: string
  at: string
}

export interface Activity {
  available: boolean
  entries: ActivityEntry[]
}

export async function getBoardStats(boardId: string, signal?: AbortSignal): Promise<StatsResult> {
  const { data, response } = await apiRequestWithResponse<BoardStats>(`/api/boards/${boardId}/stats`, { signal })
  return { stats: data, cache: (response.headers.get('X-Cache') ?? 'OFF') as CacheStatus }
}

export function getBoardActivity(boardId: string, signal?: AbortSignal): Promise<Activity> {
  return apiRequest<Activity>(`/api/boards/${boardId}/activity`, { signal })
}
