// Matches the breakpoint where the sidebar becomes the full-screen menu (brand.css, Radzen's responsive sidebar).
export const isNarrow = () => window.matchMedia("(max-width: 768px)").matches;
