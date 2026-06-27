// Deterministic generated product visual — pure CSS + one inline SVG, zero image requests.
// productType controls hue + line-art pattern + monogram (so a category reads identically
// everywhere); a hash of the SKU nudges the gradient angle + monogram offset so no two tiles
// in a category are identical. A deliberate line-art illustration system, not a placeholder.
import type { ReactNode } from 'react'

type PatternKind = 'rings' | 'pleats' | 'ribs' | 'diamond' | 'circuit' | 'arcs' | 'grid'

interface CategoryStyle {
  hue: number
  pattern: PatternKind
}

// Known categories get a bespoke pattern; unknown ones hash to a stable hue + generic grid.
const CATEGORY: Record<string, CategoryStyle> = {
  brakes: { hue: 8, pattern: 'rings' },
  filtration: { hue: 150, pattern: 'pleats' },
  filters: { hue: 150, pattern: 'pleats' },
  'power transmission': { hue: 28, pattern: 'ribs' },
  belts: { hue: 28, pattern: 'ribs' },
  bearings: { hue: 220, pattern: 'diamond' },
  fasteners: { hue: 265, pattern: 'diamond' },
  electrical: { hue: 200, pattern: 'circuit' },
  fluids: { hue: 190, pattern: 'arcs' },
  lubricants: { hue: 45, pattern: 'arcs' },
  'workshop consumables': { hue: 322, pattern: 'grid' },
  wipers: { hue: 250, pattern: 'ribs' },
}

function hash(value: string): number {
  let h = 0
  for (let i = 0; i < value.length; i++) h = (Math.imul(h, 31) + value.charCodeAt(i)) | 0
  return Math.abs(h)
}

function styleFor(productType: string): CategoryStyle {
  const key = (productType ?? '').trim().toLowerCase()
  return CATEGORY[key] ?? { hue: hash(key || 'part') % 360, pattern: 'grid' }
}

function monogram(productType: string): string {
  const words = (productType ?? '').trim().split(/\s+/).filter(Boolean)
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase()
  return (words[0] ?? 'PT').slice(0, 2).toUpperCase()
}

function PatternDefs({ kind, id }: { kind: PatternKind; id: string }) {
  const common = { stroke: 'currentColor', strokeWidth: 1, fill: 'none', strokeOpacity: 0.6 }
  switch (kind) {
    case 'rings':
      return (
        <pattern id={id} width="64" height="64" patternUnits="userSpaceOnUse">
          <circle cx="32" cy="32" r="9" {...common} />
          <circle cx="32" cy="32" r="18" {...common} />
          <circle cx="32" cy="32" r="27" {...common} />
        </pattern>
      )
    case 'pleats':
      return (
        <pattern id={id} width="22" height="24" patternUnits="userSpaceOnUse">
          <path d="M0 24 L11 4 L22 24" {...common} />
        </pattern>
      )
    case 'ribs':
      return (
        <pattern id={id} width="16" height="16" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <line x1="0" y1="0" x2="0" y2="16" {...common} />
          <line x1="8" y1="0" x2="8" y2="16" {...common} />
        </pattern>
      )
    case 'diamond':
      return (
        <pattern id={id} width="26" height="26" patternUnits="userSpaceOnUse">
          <path d="M13 2 L24 13 L13 24 L2 13 Z" {...common} />
        </pattern>
      )
    case 'circuit':
      return (
        <pattern id={id} width="48" height="48" patternUnits="userSpaceOnUse">
          <path d="M0 24 H16 V8 H48 M16 24 V40 H40 M32 8 V0 M32 40 V48" {...common} />
          <circle cx="16" cy="24" r="2.4" {...common} />
          <circle cx="32" cy="8" r="2.4" {...common} />
          <circle cx="40" cy="40" r="2.4" {...common} />
        </pattern>
      )
    case 'arcs':
      return (
        <pattern id={id} width="44" height="22" patternUnits="userSpaceOnUse">
          <path d="M0 22 Q22 -2 44 22" {...common} />
          <path d="M-22 22 Q0 -2 22 22" {...common} />
          <path d="M22 22 Q44 -2 66 22" {...common} />
        </pattern>
      )
    case 'grid':
    default:
      return (
        <pattern id={id} width="22" height="22" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <path d="M0 0 H22 M0 0 V22" {...common} />
        </pattern>
      )
  }
}

export function ProductMedia({
  productType,
  sku,
  variant = 'card',
  children,
}: {
  productType: string
  sku: string
  variant?: 'card' | 'thumb'
  children?: ReactNode
}) {
  const { hue, pattern } = styleFor(productType)
  const seed = hash(sku || productType || 'x')
  const angle = 115 + (seed % 50) - 25
  const mx = 40 + (seed % 24) // monogram horizontal % center-ish
  const my = 38 + ((seed >> 3) % 24)
  const patternId = `pat-${pattern}-${seed}`
  const isThumb = variant === 'thumb'

  // Low-saturation tints keep the catalog near-monochrome/editorial — category hue reads as a
  // subtle warm wash, leaving cobalt as the only true accent.
  const wash = `linear-gradient(${angle}deg, hsl(${hue} 16% 94%), hsl(${(hue + 24) % 360} 13% 88%))`
  const monoSize = isThumb ? 22 : 64

  return (
    <div className={`media${isThumb ? ' thumb' : ''}`} role="img" aria-label={`${productType} part`}>
      <div className="wash" style={{ background: wash }} />
      {!isThumb && (
        <svg className="pattern" width="100%" height="100%" aria-hidden="true">
          <defs>
            <PatternDefs kind={pattern} id={patternId} />
          </defs>
          <rect width="100%" height="100%" fill={`url(#${patternId})`} />
        </svg>
      )}
      <span
        className="monogram"
        style={{
          fontSize: monoSize,
          left: isThumb ? '50%' : `${mx}%`,
          top: isThumb ? '50%' : `${my}%`,
          transform: 'translate(-50%, -50%)',
        }}
      >
        {monogram(productType)}
      </span>
      {!isThumb && (
        <>
          <span className="ticks" aria-hidden="true">
            <i className="tick tl" />
            <i className="tick tr" />
            <i className="tick bl" />
            <i className="tick br" />
          </span>
          {children && <div className="band-anchor">{children}</div>}
          <span className="sku-stamp">{sku}</span>
        </>
      )}
    </div>
  )
}
