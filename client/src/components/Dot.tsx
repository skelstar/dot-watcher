interface Props {
  colour: string
  hidden?: boolean
}

export default function Dot({ colour, hidden }: Props) {
  if (hidden) return null
  return (
    <div style={{
      width: 10,
      height: 10,
      borderRadius: '50%',
      background: colour,
      border: '1px solid white',
      boxShadow: '0 1px 2px rgba(0,0,0,0.3)',
    }} />
  )
}
