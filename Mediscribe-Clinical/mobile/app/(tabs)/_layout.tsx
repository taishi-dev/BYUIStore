import { Tabs } from 'expo-router';
import { BottomTabBar } from '@react-navigation/bottom-tabs';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { View } from 'react-native';
import Ionicons from '@expo/vector-icons/Ionicons';

export default function TabsLayout() {
  const insets = useSafeAreaInsets();

  return (
    <Tabs
      initialRouteName="index"
      // CRITICAL: DO NOT REMOVE this tabBar prop — it auto-hides the tab bar when ≤1 visible tab.
      // Copy this exact pattern when rewriting this file. Single-tab bars look broken without it.
      tabBar={(props) => {
        const visibleRoutes = props.state.routes.filter(
          (route) => props.descriptors[route.key]?.options?.href !== null
        );
        if (visibleRoutes.length <= 1) {
          return <View style={{ paddingBottom: insets.bottom }} />;
        }
        return <BottomTabBar {...props} />;
      }}
      screenOptions={{
        headerShown: false,
        sceneStyle: {
          backgroundColor: '#FFFFFF',
        },
        tabBarActiveTintColor: '#007AFF',
        tabBarInactiveTintColor: '#8E8E93',
        tabBarStyle: {
          backgroundColor: '#FFFFFF',
          borderTopColor: '#E5E5EA',
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Dashboard',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="grid" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="new-session"
        options={{
          title: 'New Session',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="add-circle" size={size} color={color} />
          ),
        }}
      />
    </Tabs>
  );
}
