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
        text: { mutedColor: '{slate.600}', hoverMutedColor: '{slate.700}' },
        formField: { borderColor: '{slate.400}', hoverBorderColor: '{slate.500}' },
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
      colorScheme: {
        light: {
          root: {
            secondary: {
              background: '{slate.100}', hoverBackground: '{slate.200}', activeBackground: '{slate.300}',
              borderColor: '{slate.300}', hoverBorderColor: '{slate.400}', activeBorderColor: '{slate.500}',
              color: '{slate.900}', hoverColor: '{slate.950}', activeColor: '{slate.950}',
            },
          },
          outlined: {
            primary: { borderColor: '{cyan.600}' },
            secondary: { borderColor: '{slate.400}', color: '{slate.700}', hoverBackground: '{slate.100}', activeBackground: '{slate.200}' },
          },
          text: {
            secondary: { color: '{slate.700}', hoverBackground: '{slate.200}', activeBackground: '{slate.300}' },
          },
        },
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
        sm: { fontSize: '0.8125rem' },
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
    tree: {
      colorScheme: {
        light: {
          node: {
            selectedBackground: 'color-mix(in srgb, {cyan.100} 50%, {slate.50})',
            selectedColor: '{slate.700}',
          },
          nodeIcon: {
            selectedColor: '{cyan.700}',
          },
          nodeToggleButton: {
            selectedHoverBackground: '{cyan.100}',
            selectedHoverColor: '{cyan.800}',
          },
        },
        dark: {
          node: {
            selectedBackground: 'color-mix(in srgb, {cyan.300} 10%, {slate.900})',
            selectedColor: '{slate.200}',
          },
          nodeIcon: {
            selectedColor: '{cyan.300}',
          },
          nodeToggleButton: {
            selectedHoverBackground: 'color-mix(in srgb, {cyan.300} 18%, {slate.900})',
            selectedHoverColor: '{cyan.200}',
          },
        },
      },
    },
  },
})
