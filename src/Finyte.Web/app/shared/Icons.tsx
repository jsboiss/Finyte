import { Activity as ActivityGlyph, ArrowRightLeft as ArrowRightLeftGlyph, Banknote as BanknoteGlyph, CalendarDays as CalendarDaysGlyph, ChevronDown as ChevronDownGlyph, ChevronLeft as ChevronLeftGlyph, ChevronRight as ChevronRightGlyph, CircleHelp as CircleHelpGlyph, CreditCard as CreditCardGlyph, Ellipsis as EllipsisGlyph, Home as HomeGlyph, Loader2 as Loader2Glyph, Mail as MailGlyph, Menu as MenuGlyph, Plus as PlusGlyph, ReceiptText as ReceiptTextGlyph, RefreshCcw as RefreshCcwGlyph, Settings as SettingsGlyph, Shield as ShieldGlyph, SlidersHorizontal as SlidersHorizontalGlyph, Tags as TagsGlyph, Trash2 as Trash2Glyph, Upload as UploadGlyph, UserPlus as UserPlusGlyph, Users as UsersGlyph, WalletCards as WalletCardsGlyph, X as XGlyph, type LucideIcon, type LucideProps } from 'lucide-react'

// All icons inherit currentColor. Labels are required only when the icon conveys meaning alone.
type IconProps = LucideProps & { label?: string }
function StandardIcon({ glyph: Glyph, label, size = 20, strokeWidth = 1.75, ...props }: IconProps & { glyph: LucideIcon }) {
  return <Glyph {...props} size={size} strokeWidth={strokeWidth} focusable="false" aria-hidden={label ? undefined : true} role={label ? 'img' : undefined} aria-label={label} />
}
export function Activity(props: IconProps) { return <StandardIcon glyph={ActivityGlyph} {...props} /> }
export function ArrowRightLeft(props: IconProps) { return <StandardIcon glyph={ArrowRightLeftGlyph} {...props} /> }
export function Banknote(props: IconProps) { return <StandardIcon glyph={BanknoteGlyph} {...props} /> }
export function CalendarDays(props: IconProps) { return <StandardIcon glyph={CalendarDaysGlyph} {...props} /> }
export function ChevronDown(props: IconProps) { return <StandardIcon glyph={ChevronDownGlyph} {...props} /> }
export function ChevronLeft(props: IconProps) { return <StandardIcon glyph={ChevronLeftGlyph} {...props} /> }
export function ChevronRight(props: IconProps) { return <StandardIcon glyph={ChevronRightGlyph} {...props} /> }
export function CircleHelp(props: IconProps) { return <StandardIcon glyph={CircleHelpGlyph} {...props} /> }
export function CreditCard(props: IconProps) { return <StandardIcon glyph={CreditCardGlyph} {...props} /> }
export function Ellipsis(props: IconProps) { return <StandardIcon glyph={EllipsisGlyph} {...props} /> }
export function Home(props: IconProps) { return <StandardIcon glyph={HomeGlyph} {...props} /> }
export function Loader2(props: IconProps) { return <StandardIcon glyph={Loader2Glyph} {...props} /> }
export function Mail(props: IconProps) { return <StandardIcon glyph={MailGlyph} {...props} /> }
export function Menu(props: IconProps) { return <StandardIcon glyph={MenuGlyph} {...props} /> }
export function Plus(props: IconProps) { return <StandardIcon glyph={PlusGlyph} {...props} /> }
export function ReceiptText(props: IconProps) { return <StandardIcon glyph={ReceiptTextGlyph} {...props} /> }
export function RefreshCcw(props: IconProps) { return <StandardIcon glyph={RefreshCcwGlyph} {...props} /> }
export function Settings(props: IconProps) { return <StandardIcon glyph={SettingsGlyph} {...props} /> }
export function Shield(props: IconProps) { return <StandardIcon glyph={ShieldGlyph} {...props} /> }
export function SlidersHorizontal(props: IconProps) { return <StandardIcon glyph={SlidersHorizontalGlyph} {...props} /> }
export function Tags(props: IconProps) { return <StandardIcon glyph={TagsGlyph} {...props} /> }
export function Trash2(props: IconProps) { return <StandardIcon glyph={Trash2Glyph} {...props} /> }
export function Upload(props: IconProps) { return <StandardIcon glyph={UploadGlyph} {...props} /> }
export function UserPlus(props: IconProps) { return <StandardIcon glyph={UserPlusGlyph} {...props} /> }
export function Users(props: IconProps) { return <StandardIcon glyph={UsersGlyph} {...props} /> }
export function WalletCards(props: IconProps) { return <StandardIcon glyph={WalletCardsGlyph} {...props} /> }
export function X(props: IconProps) { return <StandardIcon glyph={XGlyph} {...props} /> }
const iconMap = { dashboard: Home, transactions: ReceiptText, accounts: Banknote, transfers: ArrowRightLeft, recurring: CalendarDays, imports: Upload, settings: Settings, billing: CreditCard, connections: Activity, help: CircleHelp }
export type IconName = keyof typeof iconMap
export function AppIcon({ name, ...props }: IconProps & { name: IconName }) {
  const Icon = iconMap[name] ?? CircleHelp
  return <Icon {...props} />
}
