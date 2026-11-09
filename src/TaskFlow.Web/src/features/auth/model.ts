export interface User {
  id: string
  username: string
  email: string
  displayName: string
}

export interface AuthResponse {
  token: string
  expiresAt: string
  username: string
  user: User
}

export interface LoginInput {
  username: string
  password: string
}

export interface RegisterInput {
  username: string
  email: string
  displayName: string
  password: string
}
