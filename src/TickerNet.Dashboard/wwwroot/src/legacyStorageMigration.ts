// Imported first in main.ts so old TickerQ storage keys are renamed before any store or service
// reads them.
import { migrateLegacyBrowserStorage } from './utilities/legacyStorage'

migrateLegacyBrowserStorage()
