import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

const slateSurface = {
  0: '#ffffff',
  50: '{slate.50}',
  100: '{slate.100}',
  200: '{slate.200}',
  300: '{slate.300}',
  400: '{slate.400}',
  500: '{slate.500}',
  600: '{slate.600}',
  700: '{slate.700}',
  800: '{slate.800}',
  900: '{slate.900}',
  950: '{slate.950}',
}

export const nexusPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{cyan.50}',
      100: '{cyan.100}',
      200: '{cyan.200}',
      300: '{cyan.300}',
      400: '{cyan.400}',
      500: '{cyan.500}',
      600: '{cyan.600}',
      700: '{cyan.700}',
      800: '{cyan.800}',
      900: '{cyan.900}',
      950: '{cyan.950}',
    },
    formField: {
      borderRadius: '0.375rem',
      sm: { fontSize: '0.8125rem', paddingX: '0.75rem', paddingY: '0.375rem' },
    },
    colorScheme: {
      light: {
        surface: slateSurface,
        primary: { color: '{cyan.700}', hoverColor: '{cyan.800}', activeColor: '{cyan.900}', contrastColor: '#ffffff' },
      },
      dark: {
        surface: slateSurface,
        primary: { color: '{cyan.300}', hoverColor: '{cyan.200}', activeColor: '{cyan.100}', contrastColor: '{slate.950}' },
      },
    },
  },
  components: {
    button: {
      root: {
        borderRadius: '0.375rem',
      },
    },
    checkbox: {
      root: {
        borderRadius: '0.25rem',
      },
    },
    dialog: {
      root: {
        borderRadius: '0.5rem',
      },
    },
    menu: {
      root: {
        borderRadius: '0.375rem',
      },
      item: {
        borderRadius: '0.25rem',
      },
    },
    select: {
      root: {
        borderRadius: '0.375rem',
      },
      overlay: {
        borderRadius: '0.375rem',
      },
      option: {
        borderRadius: '0.25rem',
      },
    },
    tabs: {
      tab: {
        padding: '0.5rem 0.75rem',
      },
    },
  },
})
