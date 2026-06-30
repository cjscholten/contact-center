// Gedeeld Zeta-design system + app-infrastructuur voor ZetaDesk (agent) en ZetaBeheer (admin).
export { zetaTheme } from './theme';
export { AppProviders } from './AppProviders';
export {
  resolveTenant,
  realmForTenant,
  buildOidcConfig,
  tenant,
  apiBase,
  type OidcConfig,
} from './tenant';
export {
  setAccessToken,
  getAccessToken,
  authHeader,
  realmRolesFromToken,
} from './auth/token';
export {
  Centered,
  LoadingScreen,
  LoginScreen,
  AuthErrorScreen,
  AccessDeniedScreen,
} from './screens';
