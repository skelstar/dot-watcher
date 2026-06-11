interface Props {
  colour: string
}

export default function Dot({ colour }: Props) {
  return (
    <div style={{
      width: 14,
      height: 14,
      borderRadius: '50%',
      background: colour,
      border: '2px solid white',
      boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
    }} />
  )
}
