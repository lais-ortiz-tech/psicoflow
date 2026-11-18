import { Fragment } from 'react'

type NumericTextProps = { value: string | number }

export default function NumericText({ value }: NumericTextProps) {
  const parts = String(value).split(/(\d[\d.,]*)/g)

  return (
    <>
      {parts.map((part, index) => (/^\d/.test(part)
        ? <span className="numeric-glyph" key={index}>{part}</span>
        : <Fragment key={index}>{part}</Fragment>))}
    </>
  )
}
