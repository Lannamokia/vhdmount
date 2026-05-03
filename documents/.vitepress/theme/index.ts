import DefaultTheme from 'vitepress/theme'
import RecoveryPasswordGenerator from './components/RecoveryPasswordGenerator.vue'
import './custom.css'

export default {
  ...DefaultTheme,
  enhanceApp({ app }) {
    app.component('RecoveryPasswordGenerator', RecoveryPasswordGenerator)
  }
}
